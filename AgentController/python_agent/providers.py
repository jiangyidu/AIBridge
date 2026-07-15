# -*- coding: utf-8 -*-
"""Provider adapters.

AgentCore consumes a single normalized response shape regardless of provider:
{
  "content": str,
  "reasoning_content": str,
  "tool_calls": [OpenAI-compatible tool call dicts]
}
"""

import json
from typing import Any, Dict, List, Optional

import requests


def _clean_openai_messages(messages: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    cleaned = []
    for msg in messages:
        item = {k: v for k, v in msg.items() if k not in ("reasoning_content",)}
        if msg.get("reasoning_content"):
            item["reasoning_content"] = msg["reasoning_content"]
        if not item.get("tool_calls"):
            item.pop("tool_calls", None)
        cleaned.append(item)
    return cleaned


class BaseProvider:
    def __init__(self, config: Dict[str, Any]):
        self.config = config

    def chat(self, messages: List[Dict[str, Any]], tools: List[Dict[str, Any]]) -> Dict[str, Any]:
        raise NotImplementedError


class OpenAICompatibleProvider(BaseProvider):
    def chat(self, messages: List[Dict[str, Any]], tools: List[Dict[str, Any]]) -> Dict[str, Any]:
        api_url = self.config.get("api_url") or ""
        api_key = self.config.get("api_key") or ""
        model = self.config.get("model") or ""
        headers = {"Content-Type": "application/json"}
        if api_key:
            headers["Authorization"] = f"Bearer {api_key}"
        body = {
            "model": model,
            "max_tokens": int(self.config.get("max_tokens", 8192)),
            "messages": _clean_openai_messages(messages),
            "tools": tools or [],
        }
        response = requests.post(api_url, headers=headers, json=body, timeout=120)
        response.raise_for_status()
        raw = response.json()
        choice = (raw.get("choices") or [{}])[0]
        msg = choice.get("message") or {}
        return {
            "content": msg.get("content") or "",
            "reasoning_content": msg.get("reasoning_content") or "",
            "tool_calls": msg.get("tool_calls") or [],
            "raw": raw,
        }


def _openai_tools_to_anthropic(tools: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    result = []
    for tool in tools or []:
        fn = tool.get("function", {}) if tool.get("type") == "function" else tool
        name = fn.get("name")
        if not name:
            continue
        result.append({
            "name": name,
            "description": fn.get("description", ""),
            "input_schema": fn.get("parameters", {"type": "object", "properties": {}}),
        })
    return result


def _messages_to_anthropic(messages: List[Dict[str, Any]]) -> (str, List[Dict[str, Any]]):
    system = ""
    converted = []
    for msg in messages:
        role = msg.get("role")
        if role == "system":
            system = (system + "\n\n" + (msg.get("content") or "")).strip()
            continue
        if role == "tool":
            converted.append({
                "role": "user",
                "content": [{
                    "type": "tool_result",
                    "tool_use_id": msg.get("tool_call_id") or "",
                    "content": msg.get("content") or "",
                }],
            })
            continue
        if role == "assistant" and msg.get("tool_calls"):
            content = []
            if msg.get("content"):
                content.append({"type": "text", "text": msg.get("content") or ""})
            for call in msg.get("tool_calls") or []:
                fn = call.get("function") or {}
                args = fn.get("arguments") or "{}"
                try:
                    input_obj = json.loads(args) if isinstance(args, str) else args
                except Exception:
                    input_obj = {"_raw_arguments": str(args)}
                content.append({
                    "type": "tool_use",
                    "id": call.get("id") or fn.get("name") or "tool_call",
                    "name": fn.get("name") or "",
                    "input": input_obj,
                })
            converted.append({"role": "assistant", "content": content})
            continue
        if role in ("user", "assistant"):
            converted.append({"role": role, "content": msg.get("content") or ""})
    return system, converted


class AnthropicProvider(BaseProvider):
    def chat(self, messages: List[Dict[str, Any]], tools: List[Dict[str, Any]]) -> Dict[str, Any]:
        base_url = (self.config.get("api_url") or self.config.get("base_url") or "https://api.anthropic.com").rstrip("/")
        api_key = self.config.get("api_key") or ""
        model = self.config.get("model") or "claude-3-5-sonnet-20241022"
        system, anthropic_messages = _messages_to_anthropic(messages)
        body = {
            "model": model,
            "max_tokens": int(self.config.get("max_tokens", 8192)),
            "messages": anthropic_messages,
            "tools": _openai_tools_to_anthropic(tools),
        }
        if system:
            body["system"] = system
        headers = {
            "Content-Type": "application/json",
            "x-api-key": api_key,
            "anthropic-version": "2023-06-01",
        }
        response = requests.post(base_url + "/v1/messages", headers=headers, json=body, timeout=120)
        response.raise_for_status()
        raw = response.json()
        text_parts: List[str] = []
        tool_calls = []
        for block in raw.get("content", []) or []:
            if block.get("type") == "text":
                text_parts.append(block.get("text") or "")
            elif block.get("type") == "tool_use":
                tool_calls.append({
                    "id": block.get("id") or block.get("name") or "tool_call",
                    "type": "function",
                    "function": {
                        "name": block.get("name") or "",
                        "arguments": json.dumps(block.get("input") or {}, ensure_ascii=False),
                    },
                })
        return {
            "content": "\n".join([p for p in text_parts if p]),
            "reasoning_content": "",
            "tool_calls": tool_calls,
            "raw": raw,
        }


def create_provider(config: Dict[str, Any]) -> BaseProvider:
    provider = (config.get("provider") or "").lower()
    api_url = (config.get("api_url") or "").lower()
    if provider == "claude" or "anthropic.com" in api_url:
        return AnthropicProvider(config)
    return OpenAICompatibleProvider(config)
