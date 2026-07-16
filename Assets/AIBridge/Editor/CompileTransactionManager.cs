using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using LitJson;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace AIBridge.Agent
{
    /// <summary>每个脚本域都有唯一标识，用于证明成功编译后确实发生过 Domain Reload。</summary>
    [InitializeOnLoad]
    public static class AgentDomainIdentity
    {
        public static readonly string Token = Guid.NewGuid().ToString("N");
        public static string Current { get { return Token; } }

        static AgentDomainIdentity() { }
    }

    /// <summary>
    /// 生成源码的两阶段事务。状态先写入 Library，再改 Assets；编译失败时先记录原始错误，
    /// 再恢复备份并等待第二次编译/重载，避免坏脚本永久污染项目。
    /// </summary>
    [InitializeOnLoad]
    public static class CompileTransactionManager
    {
        private const double CompileTimeoutSeconds = 120.0;

        static CompileTransactionManager()
        {
            if (AgentEditorEnvironment.IsAssetImportWorker) return;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        public static string BeginGeneratedSource(
            string operationId,
            string sourcePath,
            string sourceText,
            string targetKind,
            string targetName,
            string targetTypeName)
        {
            try
            {
                if (EditorApplication.isCompiling)
                    return AgentJson.Error("Unity 正在编译，不能同时启动新的代码生成事务");

                operationId = string.IsNullOrEmpty(operationId) ? Guid.NewGuid().ToString("N") : operationId;
                sourcePath = Path.GetFullPath(sourcePath ?? "");
                string assetsPath = Path.GetFullPath(Application.dataPath);
                if (!IsUnderDirectory(sourcePath, assetsPath))
                    return AgentJson.Error("拒绝写入 Assets 目录之外的源码: " + sourcePath);

                AgentCompileTransactionState existing = AgentStateStore.LoadCompile();
                if (existing != null && !IsTerminal(existing.phase))
                {
                    if (existing.operationId == operationId)
                        return BuildPendingJson(existing);
                    return AgentJson.Error("已有代码编译事务正在执行: " + existing.operationId);
                }
                if (existing != null && IsTerminal(existing.phase))
                {
                    CleanupBackup(existing);
                    AgentStateStore.DeleteCompileState();
                }

                string directory = Path.GetDirectoryName(sourcePath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                AgentCompileTransactionState state = new AgentCompileTransactionState();
                state.operationId = operationId;
                state.sourcePath = sourcePath;
                state.assetPath = ToAssetPath(sourcePath);
                state.targetKind = targetKind ?? "";
                state.targetName = targetName ?? "";
                state.targetTypeName = targetTypeName ?? targetName ?? "";
                state.sourceExisted = File.Exists(sourcePath);
                state.generatedHash = ComputeHash(sourceText ?? "");
                state.originalHash = state.sourceExisted ? ComputeFileHash(sourcePath) : "";
                state.originDomainId = AgentDomainIdentity.Token;
                state.startedUtcTicks = DateTime.UtcNow.Ticks;
                state.phaseStartedUtcTicks = state.startedUtcTicks;
                state.phase = CompileTransactionPhase.Prepared;

                if (state.sourceExisted)
                {
                    string backupDirectory = Path.Combine(AgentStateStore.StateDirectory, "backups");
                    if (!Directory.Exists(backupDirectory)) Directory.CreateDirectory(backupDirectory);
                    state.backupPath = Path.Combine(backupDirectory, operationId + ".cs.bak");
                    File.Copy(sourcePath, state.backupPath, true);
                }

                // 关键顺序：Prepared 必须先落盘，之后才能碰 Assets。
                AgentStateStore.SaveCompile(state);
                WriteSourceAtomically(sourcePath, sourceText ?? "", operationId);

                SetPhase(state, CompileTransactionPhase.AwaitingCompilation,
                    "源码已写入，等待 Unity 编译并重载程序集");
                state.compileCycleStarted = false;
                state.compileCycleFinished = false;
                state.rollbackCycle = false;
                AgentStateStore.SaveCompile(state);

                // Refresh 可能在返回前触发 Domain Reload；所有恢复信息已在此之前保存。
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                return BuildPendingJson(state);
            }
            catch (Exception ex)
            {
                AgentCompileTransactionState failed = AgentStateStore.LoadCompile();
                if (failed != null && failed.operationId == operationId && !IsTerminal(failed.phase))
                {
                    SetPhase(failed, CompileTransactionPhase.CompilationFailed,
                        "写入生成源码失败，准备恢复原文件: " + ex.Message);
                    AgentStateStore.SaveCompile(failed);
                }
                return AgentJson.Error("启动代码编译事务失败: " + ex.Message, ex);
            }
        }

        public static void OnCompilationStarted()
        {
            AgentCompileTransactionState state = AgentStateStore.LoadCompile();
            if (state == null || IsTerminal(state.phase)) return;
            if (state.phase != CompileTransactionPhase.AwaitingCompilation &&
                state.phase != CompileTransactionPhase.AwaitingRollbackCompilation) return;

            state.compileCycleStarted = true;
            state.compileCycleFinished = false;
            state.rollbackCycle = state.phase == CompileTransactionPhase.AwaitingRollbackCompilation;
            state.currentErrors = new List<AgentCompilerErrorState>();
            state.detail = state.rollbackCycle ? "回滚后的恢复编译已开始" : "生成源码编译已开始";
            AgentStateStore.SaveCompile(state);
        }

        public static void OnCompilationFinished(IList<CompilerMessage> errors)
        {
            AgentCompileTransactionState state = AgentStateStore.LoadCompile();
            if (state == null || IsTerminal(state.phase) || !state.compileCycleStarted) return;

            state.compileCycleFinished = true;
            state.currentErrors = ConvertErrors(errors);
            state.compileFinishedDomainId = AgentDomainIdentity.Token;

            if (state.rollbackCycle)
            {
                if (state.currentErrors.Count > 0)
                {
                    SetPhase(state, CompileTransactionPhase.FailedRollback,
                        "回滚后工程仍然编译失败，已停止自动处理");
                }
                else
                {
                    SetPhase(state, CompileTransactionPhase.RollbackCompiledAwaitingReload,
                        "回滚源码编译成功，等待 Domain Reload 完成");
                }
            }
            else if (state.currentErrors.Count > 0)
            {
                state.originalErrors = new List<AgentCompilerErrorState>(state.currentErrors);
                SetPhase(state, CompileTransactionPhase.CompilationFailed,
                    "生成源码编译失败，准备恢复原文件");
            }
            else
            {
                SetPhase(state, CompileTransactionPhase.CompiledAwaitingReload,
                    "生成源码编译成功，等待 Domain Reload 后验证目标类型");
            }

            AgentStateStore.SaveCompile(state);
        }

        /// <summary>
        /// Unity 2018.4 没有全局 compilationFinished。每个程序集完成后先持久化累计错误，
        /// 即使最后一个程序集完成后立即 Domain Reload，新脚本域也能补完事务。
        /// </summary>
        public static void OnCompilationProgress(IList<CompilerMessage> errors)
        {
            AgentCompileTransactionState state = AgentStateStore.LoadCompile();
            if (state == null || IsTerminal(state.phase) || !state.compileCycleStarted) return;
            state.currentErrors = ConvertErrors(errors);
            state.detail = state.rollbackCycle
                ? "正在累计恢复编译结果" : "正在累计生成源码编译结果";
            AgentStateStore.SaveCompile(state);
        }

        public static string GetStatusJson(string operationId, out bool isFinal)
        {
            Tick();
            AgentCompileTransactionState state = AgentStateStore.LoadCompile();
            isFinal = false;
            if (state == null)
            {
                JsonData idle = AgentJson.NewObject();
                idle["Success"] = true;
                idle["Status"] = "idle";
                idle["Message"] = "当前没有代码编译事务";
                return JsonMapper.ToJson(idle);
            }
            if (!string.IsNullOrEmpty(operationId) && state.operationId != operationId)
            {
                isFinal = true;
                return AgentJson.Error("编译事务不匹配，期望 " + operationId + "，实际 " + state.operationId);
            }

            if (state.phase == CompileTransactionPhase.Succeeded)
            {
                isFinal = true;
                JsonData done = AgentJson.NewObject();
                done["Success"] = true;
                done["Status"] = "done";
                done["OperationId"] = state.operationId;
                done["Message"] = state.targetKind == "method"
                    ? "方法 " + state.targetName + " 已编译并在新脚本域中加载"
                    : "脚本类型 " + state.targetName + " 已编译并在新脚本域中加载";
                done["TargetName"] = state.targetName;
                done["TargetKind"] = state.targetKind;
                done["SourcePath"] = state.assetPath;
                return JsonMapper.ToJson(done);
            }

            if (IsFailure(state.phase))
            {
                isFinal = true;
                JsonData failed = AgentJson.NewObject();
                failed["Success"] = false;
                failed["Status"] = "error";
                failed["OperationId"] = state.operationId;
                failed["Message"] = state.detail;
                failed["RollbackSucceeded"] = state.phase == CompileTransactionPhase.FailedRestored;
                failed["CompileErrors"] = BuildErrorsArray(
                    state.originalErrors != null && state.originalErrors.Count > 0
                        ? state.originalErrors : state.currentErrors);
                return JsonMapper.ToJson(failed);
            }

            return BuildPendingJson(state);
        }

        public static void Consume(string operationId)
        {
            AgentCompileTransactionState state = AgentStateStore.LoadCompile();
            if (state == null || state.operationId != operationId || !IsTerminal(state.phase)) return;
            CleanupBackup(state);
            AgentStateStore.DeleteCompileState();
        }

        public static void Tick()
        {
            AgentCompileTransactionState state = AgentStateStore.LoadCompile();
            if (state == null || IsTerminal(state.phase)) return;

            // 2018.4 成功编译可能在 Editor update 收口前直接重载。周期已开始、脚本域已变化，
            // 即可用逐程序集持久化的错误补完最终状态。
            if ((state.phase == CompileTransactionPhase.AwaitingCompilation ||
                 state.phase == CompileTransactionPhase.AwaitingRollbackCompilation) &&
                state.originDomainId != AgentDomainIdentity.Token &&
                !EditorApplication.isCompiling)
            {
                state.compileCycleFinished = true;
                state.compileFinishedDomainId = state.originDomainId;
                bool rollbackSourceMatches = !state.rollbackCycle ||
                    (state.sourceExisted
                        ? File.Exists(state.sourcePath) && ComputeFileHash(state.sourcePath) == state.originalHash
                        : !File.Exists(state.sourcePath));
                if (!rollbackSourceMatches)
                {
                    SetPhase(state, CompileTransactionPhase.FailedRollback,
                        "新脚本域恢复后，源码哈希与生成前状态不一致");
                }
                else if (state.currentErrors != null && state.currentErrors.Count > 0)
                {
                    if (state.rollbackCycle)
                    {
                        SetPhase(state, CompileTransactionPhase.FailedRollback,
                            "新脚本域恢复时发现回滚编译仍有错误");
                    }
                    else
                    {
                        state.originalErrors = new List<AgentCompilerErrorState>(state.currentErrors);
                        SetPhase(state, CompileTransactionPhase.CompilationFailed,
                            "新脚本域恢复时发现生成源码编译错误");
                    }
                }
                else if (state.rollbackCycle)
                {
                    SetPhase(state, CompileTransactionPhase.RollbackCompiledAwaitingReload,
                        "回滚编译成功，已在新脚本域恢复");
                }
                else
                {
                    SetPhase(state, CompileTransactionPhase.CompiledAwaitingReload,
                        "生成源码编译成功，已在新脚本域恢复");
                }
                AgentStateStore.SaveCompile(state);
            }

            if (state.phase == CompileTransactionPhase.Prepared)
            {
                // 若在源码写入后、阶段更新前发生了意外重载，通过哈希自动接力。
                if (File.Exists(state.sourcePath) && ComputeFileHash(state.sourcePath) == state.generatedHash)
                {
                    SetPhase(state, CompileTransactionPhase.AwaitingCompilation,
                        "检测到已写入的生成源码，恢复等待编译");
                    AgentStateStore.SaveCompile(state);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                }
                else
                {
                    double preparedElapsed = new TimeSpan(DateTime.UtcNow.Ticks - state.phaseStartedUtcTicks).TotalSeconds;
                    if (preparedElapsed > CompileTimeoutSeconds)
                    {
                        SetPhase(state, CompileTransactionPhase.CompilationFailed,
                            "源码写入未完成或内容校验失败，准备恢复原文件");
                        AgentStateStore.SaveCompile(state);
                    }
                }
                return;
            }

            if (state.phase == CompileTransactionPhase.CompilationFailed ||
                state.phase == CompileTransactionPhase.RollingBack)
            {
                if (EditorApplication.isCompiling) return;
                BeginOrResumeRollback(state);
                return;
            }

            if (state.phase == CompileTransactionPhase.CompiledAwaitingReload)
            {
                if (state.compileFinishedDomainId == AgentDomainIdentity.Token || EditorApplication.isCompiling) return;
                if (ValidateTarget(state))
                {
                    SetPhase(state, CompileTransactionPhase.Succeeded,
                        "编译成功并已确认目标在新脚本域中加载");
                    CleanupBackup(state);
                }
                else
                {
                    AgentCompilerErrorState missing = new AgentCompilerErrorState();
                    missing.file = state.assetPath;
                    missing.message = "编译报告成功，但重载后未找到预期目标：" + state.targetTypeName;
                    state.originalErrors = new List<AgentCompilerErrorState> { missing };
                    SetPhase(state, CompileTransactionPhase.CompilationFailed,
                        "重载后未找到预期目标，准备恢复生成前源码");
                }
                AgentStateStore.SaveCompile(state);
                return;
            }

            if (state.phase == CompileTransactionPhase.RollbackCompiledAwaitingReload)
            {
                if (state.compileFinishedDomainId == AgentDomainIdentity.Token || EditorApplication.isCompiling) return;
                SetPhase(state, CompileTransactionPhase.FailedRestored,
                    "生成源码编译失败，原文件已恢复且工程已重新编译");
                CleanupBackup(state);
                AgentStateStore.SaveCompile(state);
                return;
            }

            if (state.phase == CompileTransactionPhase.AwaitingCompilation ||
                state.phase == CompileTransactionPhase.AwaitingRollbackCompilation)
            {
                double elapsed = new TimeSpan(DateTime.UtcNow.Ticks - state.phaseStartedUtcTicks).TotalSeconds;
                if (elapsed > CompileTimeoutSeconds)
                {
                    if (state.phase == CompileTransactionPhase.AwaitingCompilation)
                    {
                        AgentCompilerErrorState timeout = new AgentCompilerErrorState();
                        timeout.file = state.assetPath;
                        timeout.message = "等待 Unity 编译超过 " + CompileTimeoutSeconds + " 秒";
                        state.originalErrors = new List<AgentCompilerErrorState> { timeout };
                        SetPhase(state, CompileTransactionPhase.CompilationFailed,
                            "编译等待超时，准备恢复原文件");
                    }
                    else
                    {
                        SetPhase(state, CompileTransactionPhase.FailedRollback,
                            "回滚后的恢复编译等待超时");
                    }
                    AgentStateStore.SaveCompile(state);
                }
            }
        }

        private static void BeginOrResumeRollback(AgentCompileTransactionState state)
        {
            try
            {
                if (state.phase != CompileTransactionPhase.RollingBack)
                {
                    SetPhase(state, CompileTransactionPhase.RollingBack,
                        "正在恢复生成前的源码状态");
                    AgentStateStore.SaveCompile(state);
                }

                bool sourceExists = File.Exists(state.sourcePath);
                if (sourceExists)
                {
                    string currentHash = ComputeFileHash(state.sourcePath);
                    if (currentHash != state.generatedHash &&
                        (!state.sourceExisted || currentHash != state.originalHash))
                    {
                        SetPhase(state, CompileTransactionPhase.FailedConflict,
                            "生成文件在编译期间被其他操作修改，为避免覆盖用户修改已停止自动回滚");
                        AgentStateStore.SaveCompile(state);
                        return;
                    }
                }

                if (state.sourceExisted)
                {
                    if (!File.Exists(state.backupPath))
                    {
                        SetPhase(state, CompileTransactionPhase.FailedRollback,
                            "原文件备份不存在，无法安全回滚");
                        AgentStateStore.SaveCompile(state);
                        return;
                    }
                    if (!sourceExists || ComputeFileHash(state.sourcePath) != state.originalHash)
                        File.Copy(state.backupPath, state.sourcePath, true);
                }
                else if (sourceExists)
                {
                    File.Delete(state.sourcePath);
                    string meta = state.sourcePath + ".meta";
                    if (File.Exists(meta)) File.Delete(meta);
                }

                SetPhase(state, CompileTransactionPhase.AwaitingRollbackCompilation,
                    "原源码已恢复，等待 Unity 重新编译");
                // 回滚是一个独立编译周期。重新记录当前脚本域，避免把第一次生成编译
                // 已发生的 Reload 误判成“回滚编译也已 Reload”。
                state.originDomainId = AgentDomainIdentity.Token;
                state.compileCycleStarted = false;
                state.compileCycleFinished = false;
                state.rollbackCycle = true;
                state.currentErrors = new List<AgentCompilerErrorState>();
                AgentStateStore.SaveCompile(state);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
            catch (Exception ex)
            {
                SetPhase(state, CompileTransactionPhase.FailedRollback,
                    "回滚生成源码失败: " + ex.Message);
                AgentStateStore.SaveCompile(state);
            }
        }

        private static bool ValidateTarget(AgentCompileTransactionState state)
        {
            Type type = AgentUtility.ResolveType(state.targetTypeName);
            if (type == null) return false;
            if (state.targetKind != "method") return true;
            MethodInfo method = type.GetMethod(state.targetName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return method != null;
        }

        private static string BuildPendingJson(AgentCompileTransactionState state)
        {
            JsonData data = AgentJson.NewObject();
            data["Success"] = true;
            data["Status"] = "compiling";
            data["OperationId"] = state.operationId;
            data["Phase"] = state.phase;
            data["Message"] = state.detail;
            data["TargetName"] = state.targetName;
            data["TargetKind"] = state.targetKind;
            data["SourcePath"] = state.assetPath;
            return JsonMapper.ToJson(data);
        }

        private static JsonData BuildErrorsArray(IList<AgentCompilerErrorState> errors)
        {
            JsonData array = AgentJson.NewArray();
            if (errors == null) return array;
            for (int i = 0; i < errors.Count; i++)
            {
                JsonData item = AgentJson.NewObject();
                item["File"] = errors[i].file ?? "";
                item["Line"] = errors[i].line;
                item["Column"] = errors[i].column;
                item["Message"] = errors[i].message ?? "";
                array.Add(item);
            }
            return array;
        }

        private static List<AgentCompilerErrorState> ConvertErrors(IList<CompilerMessage> errors)
        {
            List<AgentCompilerErrorState> result = new List<AgentCompilerErrorState>();
            if (errors == null) return result;
            for (int i = 0; i < errors.Count; i++)
            {
                if (errors[i].type != CompilerMessageType.Error) continue;
                AgentCompilerErrorState item = new AgentCompilerErrorState();
                item.file = errors[i].file ?? "";
                item.line = errors[i].line;
                item.column = errors[i].column;
                item.message = errors[i].message ?? "";
                result.Add(item);
            }
            return result;
        }

        private static void SetPhase(AgentCompileTransactionState state, string phase, string detail)
        {
            state.phase = phase;
            state.detail = detail ?? "";
            state.phaseStartedUtcTicks = DateTime.UtcNow.Ticks;
        }

        private static bool IsTerminal(string phase)
        {
            return phase == CompileTransactionPhase.Succeeded || IsFailure(phase);
        }

        private static bool IsFailure(string phase)
        {
            return phase == CompileTransactionPhase.FailedRestored ||
                   phase == CompileTransactionPhase.FailedRollback ||
                   phase == CompileTransactionPhase.FailedConflict ||
                   phase == CompileTransactionPhase.TimedOut;
        }

        private static bool IsUnderDirectory(string path, string directory)
        {
            string root = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                          + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static string ToAssetPath(string fullPath)
        {
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            string normalized = Path.GetFullPath(fullPath).Replace('\\', '/');
            if (normalized.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                return "Assets" + normalized.Substring(assets.Length);
            return normalized;
        }

        private static void WriteSourceAtomically(string path, string text, string operationId)
        {
            string temp = Path.Combine(AgentStateStore.StateDirectory, operationId + ".source.tmp");
            if (!Directory.Exists(AgentStateStore.StateDirectory))
                Directory.CreateDirectory(AgentStateStore.StateDirectory);
            // generatedHash 按无 BOM UTF-8 计算；写入必须保持相同字节序列，供重载后校验。
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                try { File.Replace(temp, path, null, true); return; }
                catch { File.Copy(temp, path, true); File.Delete(temp); return; }
            }
            File.Move(temp, path);
        }

        private static string ComputeHash(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string ComputeFileHash(string path)
        {
            try
            {
                using (SHA256 sha = SHA256.Create())
                using (FileStream stream = File.OpenRead(path))
                {
                    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                }
            }
            catch { return ""; }
        }

        private static void CleanupBackup(AgentCompileTransactionState state)
        {
            try { if (!string.IsNullOrEmpty(state.backupPath) && File.Exists(state.backupPath)) File.Delete(state.backupPath); }
            catch { }
        }
    }
}
