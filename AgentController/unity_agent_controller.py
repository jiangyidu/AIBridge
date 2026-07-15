import os
import sys
import json
import requests
import subprocess
import tempfile
import argparse
import hashlib

# ----------------- Configuration -----------------
UNITY_EXE    = r"F:\untiy\Unity\Editor\Unity.exe"
PROJECT_PATH = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
# AGENT_SERVER_URL 现由项目路径哈希动态生成，确保多开不冲突
# -------------------------------------------------

def get_project_agent_url(project_path):
    """
    功能说明：优先从 agent_port.txt 读取已启动的端口。若不存在，则根据项目绝对路径，
             通过 MD5 哈希计算出一个确定的端口（8000-9999），
             保持与 C# 端 AgentBridge.GetProjectPort() 完全一致的算法。
    """
    port_file = os.path.join(project_path, "AgentController", "agent_port.txt")
    if os.path.exists(port_file):
        try:
            with open(port_file, "r", encoding="utf-8") as f:
                port = int(f.read().strip())
                if port > 0:
                    return f"http://127.0.0.1:{port}/agent"
        except Exception:
            pass

    normalized_path = os.path.abspath(project_path).replace('\\', '/').lower()
    md5_hash = hashlib.md5(normalized_path.encode('utf-8')).digest()
    hash_uint = md5_hash[0] | (md5_hash[1] << 8) | (md5_hash[2] << 16) | (md5_hash[3] << 24)
    port = 8000 + (hash_uint % 2000)
    return f"http://127.0.0.1:{port}/agent"

def check_unity_running(agent_url):
    """
    功能说明：通过 HTTP Ping 直接检测特定项目的 Unity AgentBridge 是否可用。
    返回：
        - bool: 如果能 Ping 通则返回 True，否则返回 False。
    """
    try:
        res = requests.get(f"{agent_url}/ping", timeout=2.0)
        return res.status_code == 200
    except requests.exceptions.RequestException:
        return False


def list_commands():
    """
    功能说明：从 AgentBridge 拉取所有已注册的 [AgentCommand] 命令清单。
    原理：调用 GET /agent/commands 端点，返回所有自动扫描到的命令信息。
    返回：
        - list: 命令信息字典的列表，每项含 Key、ClassName、MethodName、Description、Category、Parameters。
        - None: 如果 Unity 未运行或请求失败。
    """
    agent_url = get_project_agent_url(PROJECT_PATH)
    if not check_unity_running(agent_url):
        print(f"[路由] 位于 {PROJECT_PATH} 的 Unity 项目在 {agent_url} 没有响应。无法获取命令列表。")
        return None
    try:
        resp = requests.get(f"{agent_url}/commands", timeout=10.0)
        data = resp.json()
        if data.get("Success"):
            return data.get("Commands", [])
        else:
            print(f"[路由] /agent/commands 返回错误：{data}")
            return None
    except Exception as e:
        print(f"[路由] 获取命令列表失败：{e}")
        return None

def wait_for_method(class_name, method_name, timeout=60):
    """
    功能说明：轮询 /agent/commands 直到指定的类和方法注册成功，用于动态编译代码后的等待。
    """
    import time
    start_time = time.time()
    agent_url = get_project_agent_url(PROJECT_PATH)
    
    print(f"[路由] 正在等待 {class_name}.{method_name} 编译并注册成功...")
    
    last_ping = False
    while time.time() - start_time < timeout:
        is_running = check_unity_running(agent_url)
        if is_running:
            if not last_ping:
                print("[路由] Unity Agent 已连接。正在检查命令...")
                last_ping = True
                
            try:
                resp = requests.get(f"{agent_url}/commands", timeout=2.0)
                if resp.status_code == 200:
                    data = resp.json()
                    commands = data.get("Commands", [])
                    for cmd in commands:
                        cmd_class = cmd.get("ClassName", "")
                        cmd_method = cmd.get("MethodName", "")
                        
                        # 支持完整类名匹配或短类名匹配
                        if (cmd_class == class_name or cmd_class.endswith("." + class_name) or class_name == cmd_class) and cmd_method == method_name:
                            print(f"[路由] 方法 {class_name}.{method_name} 现已可用！")
                            return True
            except requests.exceptions.RequestException:
                pass
        else:
            if last_ping:
                print("[路由] Unity Agent 已离线（可能正在重载域）。正在等待...")
                last_ping = False
                
        time.sleep(1.0)
        
    print(f"[路由] 等待 {class_name}.{method_name} 超时（{timeout} 秒）。")
    return False



def print_commands(commands):
    """格式化打印命令清单。"""
    if not commands:
        print("  (无可用命令，请确认 Unity 正在运行且已注册 [AgentCommand] 方法)")
        return
    # 按分类分组
    by_category = {}
    for cmd in commands:
        cat = cmd.get("Category", "General")
        by_category.setdefault(cat, []).append(cmd)

    for cat, cmds in by_category.items():
        print(f"\n  ▸ [{cat}]")
        for cmd in cmds:
            params = ", ".join(
                f"{p['Type']} {p['Name']}" for p in cmd.get("Parameters", [])
            )
            print(f"    {cmd['MethodName']}({params})")
            print(f"      → {cmd.get('Description', '')}")
            print(f"      调用：--cmd {cmd['MethodName']} --class {cmd['ClassName']}")


def execute_command(class_name, method_name, args=None):
    """
    功能说明：
        智能路由核心逻辑。将指定的任务指令下发给 Unity 执行。
    原理：
        - 路由分发：如果检测到 Unity 正在运行，采用 HTTP 极速通信向其发送指令。
        - 离线回退：如果 Unity 处于未启动的离线状态，则自动回退为 Batchmode (命令行静默模式)。
    参数：
        - class_name (str): 目标静态方法所在的完整类名。
        - method_name (str): 目标静态方法名称。
        - args (list, optional): 传递给目标方法的参数列表，默认为 None。
    返回：
        - dict: 包含任务执行结果的字典。
    """
    if args is None:
        args = []

    payload = {
        "ClassName":  class_name,
        "MethodName": method_name,
        "Args":       [str(a) for a in args]
    }

    agent_url = get_project_agent_url(PROJECT_PATH)

    # 1. 探测该项目的 Unity Agent 是否在线
    if check_unity_running(agent_url):
        print(f"[路由] Unity 项目正运行在 {agent_url}。使用极速 HTTP IPC...")
        try:
            response = requests.post(f"{agent_url}/execute", json=payload, timeout=120)
            try:
                return response.json()
            except ValueError:
                return {"Success": False,
                        "Message": f"Failed to parse JSON from HTTP response. Raw: {response.text}"}
        except requests.exceptions.RequestException as e:
            return {
                "Success": False,
                "Message": (f"Unity Agent is running but HTTP request failed: {e}\n"
                            "Check Editor.log for compile errors.")
            }

    # 2. 只有在当前项目 Agent 未运行时，才使用后台离线模式 (Batchmode CLI)
    print(f"[路由] 无法连接到位于 {agent_url} 的 Unity 项目 Agent。使用 Batchmode CLI 回退模式...")

    with tempfile.NamedTemporaryFile(mode='w', delete=False, suffix=".json", encoding='utf-8') as f_in:
        json.dump(payload, f_in, ensure_ascii=False)
        cmd_file = f_in.name

    out_file = cmd_file.replace(".json", "_out.json")

    cli_args = [
        UNITY_EXE,
        "-quit", "-batchmode",
        "-projectPath", PROJECT_PATH,
        "-executeMethod", "AIBridge.Agent.AgentBridge.ExecuteBatchTask",
        "-agentCmdFile", cmd_file,
        "-agentOutFile", out_file
    ]

    print(f"[路由] 正在运行 CLI：{' '.join(cli_args)}")

    try:
        subprocess.run(cli_args, check=False)

        if os.path.exists(out_file):
            try:
                with open(out_file, 'r', encoding='utf-8') as f_out:
                    return json.load(f_out)
            except json.JSONDecodeError as e:
                return {"Success": False,
                        "Message": f"Output file invalid JSON: {e}. Unity might have crashed."}
        else:
            return {"Success": False,
                    "Message": "Unity exited but no output file was created. Check Editor.log."}
    finally:
        if os.path.exists(cmd_file): os.remove(cmd_file)
        if os.path.exists(out_file): os.remove(out_file)


# ===================== CLI 入口 =====================

#该函数构建一个 **argparse 解析器**，用于通过命令行调用 Unity 的 AgentBridge：
#- **主解析器**：带描述文本，支持原始格式。
#- **子命令**：`dest="subcmd"` 识别子命令。
#-  `list`：列出所有已注册的 `[AgentCommand]` 命令（无参数）。
#-  `run`：执行 Unity 命令，需提供：
#-    - `--class`（必填，目标类名）
#-    - `--method`（必填，方法名）
#-    - `--args`（可选，空格分隔的参数列表）
#- 返回：`parser` 对象，供后续解析实际命令行输入。

#核心用途：为 Unity 远程命令提供 CLI 入口。
def build_cli_parser():
    parser = argparse.ArgumentParser(
        description="Unity AgentBridge 控制器 - 可直接从命令行调用 Unity 注册命令",
        formatter_class=argparse.RawTextHelpFormatter
    )
    subparsers = parser.add_subparsers(dest="subcmd")

    # list 子命令：列出所有可用命令
    list_p = subparsers.add_parser("list", help="列出所有已注册的 [AgentCommand] 命令")

    # run 子命令：执行命令
    run_p = subparsers.add_parser("run", help="执行一个 Unity 命令")
    run_p.add_argument("--class",   dest="class_name",   required=True,
                       help="目标类名（完整或短类名），例如 AntigravityTasks")
    run_p.add_argument("--method",  dest="method_name",  required=True,
                       help="目标方法名，例如 CreateCube")
    run_p.add_argument("--args",    dest="args", nargs="*", default=[],
                       help="方法参数列表（空格分隔），例如 --args 1.0 2.0 3.0")

    # wait_for_method 子命令
    wait_p = subparsers.add_parser("wait_for_method", help="等待指定的命令编译并注册完成")
    wait_p.add_argument("--class", dest="class_name", required=True)
    wait_p.add_argument("--method", dest="method_name", required=True)
    wait_p.add_argument("--timeout", dest="timeout", type=int, default=60, help="超时时间(秒)")

    # wait_and_run 子命令
    run_wait_p = subparsers.add_parser("wait_and_run", help="等待指定的命令编译完成后立即执行")
    run_wait_p.add_argument("--class",   dest="class_name",   required=True)
    run_wait_p.add_argument("--method",  dest="method_name",  required=True)
    run_wait_p.add_argument("--args",    dest="args", nargs="*", default=[])
    run_wait_p.add_argument("--timeout", dest="timeout", type=int, default=60, help="等待超时时间(秒)")

    return parser


if __name__ == "__main__":
    parser = build_cli_parser()

    # 无子命令时，走原有默认逻辑（兼容旧脚本调用方式）
    if len(sys.argv) == 1:
        print("--- Unity 双模代理控制器 ---")
        print("正在测试与 Unity 的连接...")
        res = execute_command("AntigravityTasks", "GenerateTenRotatingCubes", [])
        print("\n从 Unity 接收到的结果：")
        print(json.dumps(res, indent=2, ensure_ascii=False))
        sys.exit(0)

    args = parser.parse_args()

    if args.subcmd == "list":
        print("--- Unity AgentCommand 命令清单 ---")
        cmds = list_commands()
        print_commands(cmds)

    elif args.subcmd == "run":
        print(f"--- 执行命令: {args.class_name}.{args.method_name} ---")
        res = execute_command(args.class_name, args.method_name, args.args)
        print("\n执行结果：")
        print(json.dumps(res, indent=2, ensure_ascii=False))

    elif args.subcmd == "wait_for_method":
        success = wait_for_method(args.class_name, args.method_name, args.timeout)
        sys.exit(0 if success else 1)

    elif args.subcmd == "wait_and_run":
        print(f"--- 等待并执行命令: {args.class_name}.{args.method_name} ---")
        if wait_for_method(args.class_name, args.method_name, args.timeout):
            res = execute_command(args.class_name, args.method_name, args.args)
            print("\n执行结果：")
            print(json.dumps(res, indent=2, ensure_ascii=False))
        else:
            print("\n执行结果：")
            print(json.dumps({"Success": False, "Message": "编译方法超时"}, indent=2, ensure_ascii=False))

    else:
        parser.print_help()
