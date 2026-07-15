# -*- coding: utf-8 -*-
"""
Automated integration test for agent_loop.py
Runs a mock Unity AgentBridge server and a mock LLM server,
then executes agent_loop.py to check for protocol correctness,
handling of compile tools, polling, and ReAct loop execution.
"""

import sys
import os
import json
import time
import subprocess
import threading
import hashlib
from http.server import HTTPServer, BaseHTTPRequestHandler

# 强制重配置 stdout 的编码为 utf-8，解决 Windows CMD 的 GBK 编码报错问题
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

def get_agent_url(project_path):
    """计算 Unity Agent HTTP 地址（与 C# 端算法一致）"""
    normalized = os.path.abspath(project_path).replace('\\', '/').lower()
    md5 = hashlib.md5(normalized.encode('utf-8')).digest()
    h = md5[0] | (md5[1] << 8) | (md5[2] << 16) | (md5[3] << 24)
    port = 8000 + (h % 2000)
    return f"http://localhost:{port}/agent"

# 使用 _mock 后缀以避免与实际运行的 Unity Editor 端口冲突
script_dir = os.path.dirname(os.path.abspath(__file__))
project_dir = os.path.dirname(script_dir)
project_dir_mock = project_dir + "_mock"

agent_url_calculated = get_agent_url(project_dir_mock)
MOCK_UNITY_PORT = int(agent_url_calculated.split(":")[-1].split("/")[0])
MOCK_LLM_PORT = 9001

print(f"[Test] Mock Unity 端口: {MOCK_UNITY_PORT}", flush=True)
print(f"[Test] Mock LLM 端口: {MOCK_LLM_PORT}", flush=True)

# 状态记录
compilation_checks = 0
llm_calls = 0

class MockUnityHandler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass  # 禁用日志输出以保持控制台整洁

    def do_GET(self):
        if self.path == "/agent/ping":
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(json.dumps({"Success": True, "Message": "pong"}).encode('utf-8'))
        elif self.path == "/agent/tools_schema":
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(b"[]")
        else:
            self.send_response(404)
            self.end_headers()

    def do_POST(self):
        global compilation_checks
        if self.path == "/agent/tool":
            content_length = int(self.headers['Content-Length'])
            post_data = self.rfile.read(content_length)
            req = json.loads(post_data.decode('utf-8'))
            tool = req.get("tool")
            args = req.get("args")

            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.end_headers()

            if tool == "compile_temp_method":
                print(f"[Mock Unity] 收到 compile_temp_method, 方法名: {args.get('methodName')}", flush=True)
                res = {"Success": True, "Status": "compiling", "Message": "Compilation started..."}
            elif tool == "check_compile_status":
                compilation_checks += 1
                if compilation_checks < 2:
                    print(f"[Mock Unity] 收到 check_compile_status ({compilation_checks}), 返回: compiling", flush=True)
                    res = {"Status": "compiling", "ElapsedSeconds": 3.0, "Message": "Still compiling..."}
                else:
                    print(f"[Mock Unity] 收到 check_compile_status ({compilation_checks}), 返回: done", flush=True)
                    res = {"Status": "done", "Success": True, "Message": "Method compiled successfully."}
            else:
                res = {"Success": True, "Result": "mock_result"}

            self.wfile.write(json.dumps(res).encode('utf-8'))

class MockLLMHandler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass

    def do_POST(self):
        global llm_calls
        if self.path == "/chat/completions":
            llm_calls += 1
            content_length = int(self.headers['Content-Length'])
            post_data = self.rfile.read(content_length)
            req = json.loads(post_data.decode('utf-8'))

            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.end_headers()

            if llm_calls == 1:
                print(f"[Mock LLM] 收到第 1 次请求, 触发工具调用 compile_temp_method", flush=True)
                res = {
                    "choices": [{
                        "message": {
                            "role": "assistant",
                            "content": "我将生成并编译测试方法。",
                            "tool_calls": [{
                                "id": "call_abc123",
                                "type": "function",
                                "function": {
                                    "name": "compile_temp_method",
                                    "arguments": json.dumps({"methodName": "MyTestMethod", "code": "public static string MyTestMethod() { return \"ok\"; }"})
                                }
                            }]
                        }
                    }]
                }
            else:
                print(f"[Mock LLM] 收到第 {llm_calls} 次请求, 返回最终回答", flush=True)
                res = {
                    "choices": [{
                        "message": {
                            "role": "assistant",
                            "content": "测试方法编译并执行成功！任务已完成。",
                            "tool_calls": None
                        }
                    }]
                }

            self.wfile.write(json.dumps(res).encode('utf-8'))

def run_server(server_class, handler_class, port):
    try:
        server = server_class(("", port), handler_class)
        server.serve_forever()
    except Exception as e:
        print(f"[Test Error] 无法在端口 {port} 启动服务器: {e}", flush=True)

if __name__ == "__main__":
    # 1. 启动 Mock 服务器
    unity_thread = threading.Thread(target=run_server, args=(HTTPServer, MockUnityHandler, MOCK_UNITY_PORT), daemon=True)
    llm_thread = threading.Thread(target=run_server, args=(HTTPServer, MockLLMHandler, MOCK_LLM_PORT), daemon=True)
    
    unity_thread.start()
    llm_thread.start()
    
    print("[Test] Mock 服务器正在启动...", flush=True)
    time.sleep(2.0) # 等待服务器完全启动

    # 2. 准备配置文件 agent_config.json
    config_path = os.path.join(script_dir, "agent_config.json")
    
    # 确保 mock 项目的 AgentController 和 Assets/Editor 目录存在
    mock_assets_dir = os.path.join(project_dir_mock, "Assets", "Editor")
    mock_agent_dir = os.path.join(project_dir_mock, "AgentController")
    
    if not os.path.exists(mock_assets_dir):
        os.makedirs(mock_assets_dir)
    if not os.path.exists(mock_agent_dir):
        os.makedirs(mock_agent_dir)
        
    history_path = os.path.join(mock_agent_dir, "ai_chat_history.json")

    real_tools_path = os.path.join(project_dir, "Assets", "AIBridge", "Editor", "AgentTools.json")
    mock_tools_path = os.path.join(mock_assets_dir, "AgentTools.json")
    
    import shutil
    shutil.copyfile(real_tools_path, mock_tools_path)

    config_data = {
        "api_url": f"http://localhost:{MOCK_LLM_PORT}/chat/completions",
        "api_key": "test_key",
        "model": "test_model",
        "project_path": project_dir_mock,
        "user_message": "请编译测试方法",
        "system_prompt": "You are a helpful Unity assistant",
        "max_steps": 5,
        "compile_poll_interval": 1.0
    }

    with open(config_path, 'w', encoding='utf-8') as f:
        json.dump(config_data, f, indent=4)

    # 确保 history_path 初始为空
    if os.path.exists(history_path):
        os.remove(history_path)

    # 3. 运行 agent_loop.py
    loop_script = os.path.join(script_dir, "agent_loop.py")
    print("[Test] 启动 agent_loop.py...", flush=True)
    
    proc = subprocess.Popen(
        [sys.executable, "-u", loop_script], 
        stdout=subprocess.PIPE, 
        stderr=subprocess.STDOUT, 
        text=True, 
        encoding='utf-8'
    )
    
    print("\n--- Python Agent 输出 ---", flush=True)
    stdout_lines = []
    try:
        while True:
            line = proc.stdout.readline()
            if not line:
                break
            # 解决 Windows 控制台输出 Unicode 报错，必要时做忽略或编码转换
            try:
                print(line, end='', flush=True)
            except UnicodeEncodeError:
                print(line.encode(sys.stdout.encoding, errors='replace').decode(sys.stdout.encoding), end='', flush=True)
            stdout_lines.append(line)
        # 等待退出
        proc.wait(timeout=10)
    except subprocess.TimeoutExpired:
        print("\n[Test] 子进程运行超时！强制杀死子进程。", flush=True)
        proc.kill()
        
    stdout = "".join(stdout_lines)
    print("-------------------------\n", flush=True)

    # 4. 验证结果
    success = True
    if proc.returncode != 0:
        print(f"[FAIL] agent_loop.py 运行异常，退出码: {proc.returncode}", flush=True)
        success = False
    else:
        print("[PASS] agent_loop.py 正常退出", flush=True)

    if not os.path.exists(history_path):
        print(f"[FAIL] 找不到 ai_chat_history.json 历史记录: {history_path}", flush=True)
        success = False
    else:
        print("[PASS] ai_chat_history.json 文件已生成", flush=True)
        with open(history_path, 'r', encoding='utf-8') as f:
            hist = json.load(f)
            messages = hist.get("messages", [])
            print(f"[Test] 聊天记录共计 {len(messages)} 条消息:", flush=True)
            for m in messages:
                print(f"  - {m.get('role')}: {m.get('content')[:80]}...", flush=True)
            
            # 校验最后一条消息是 assistant 且表示成功
            if len(messages) < 3:
                print("[FAIL] 消息条数少于预期", flush=True)
                success = False
            else:
                last_msg = messages[-1]
                if last_msg.get("role") != "assistant" or "任务已完成" not in last_msg.get("content"):
                    print("[FAIL] 任务最后结果不符合预期", flush=True)
                    success = False
                else:
                    print("[PASS] 最终结果匹配成功", flush=True)

    # 5. 清理临时配置文件
    if os.path.exists(config_path):
        os.remove(config_path)
    
    # 清理 mock 的 Assets 目录
    try:
        shutil.rmtree(project_dir_mock)
    except:
        pass

    if success:
        print("\n🎉 所有集成测试用例通过！重构完全成功。", flush=True)
        sys.exit(0)
    else:
        print("\n❌ 测试失败，请检查上面的报错信息。", flush=True)
        sys.exit(1)
