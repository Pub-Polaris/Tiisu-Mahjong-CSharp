#!/usr/bin/env python3
"""把 tools/i18n/frag.<lang>.json 合并进 NET/lang/<lang>.json。

- 只维护 `tools.*` / `updater.*` / `app.*` 前缀：合并时只允许新增/更新这两个前缀下的键，
  其它前缀若和 fragment 冲突会直接报错（防止误改 Root 的 App UI 键）。
- 语言文件格式：UTF-8 无 BOM、2 空格缩进、CRLF、结尾换行（与现有文件一致）。
- `--check` 只做校验：三种语言键集合、占位符集合必须一致。

用法（在 NET 目录下）：
    python tools/i18n/merge_lang.py --check
    python tools/i18n/merge_lang.py
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LANG_DIR = ROOT / "lang"
FRAG_DIR = Path(__file__).resolve().parent
LANGUAGES = ("zh-CN", "en-US", "ja-JP")
OWNED_PREFIXES = ("tools", "updater", "app", "engine", "core")
PLACEHOLDER = re.compile(r"\{([A-Za-z_][A-Za-z0-9_]*)\}")


def load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8-sig") as handle:
        return json.load(handle)


def flatten(node: object, prefix: str = "") -> dict[str, str]:
    result: dict[str, str] = {}
    if isinstance(node, dict):
        for key, value in node.items():
            result.update(flatten(value, f"{prefix}.{key}" if prefix else str(key)))
    elif isinstance(node, str):
        result[prefix] = node
    else:
        raise TypeError(f"语言目录只允许字符串叶子：{prefix} -> {type(node).__name__}")
    return result


def walk_owned(node: object, prefix: str = "") -> None:
    """确认 fragment 里的每个叶子都落在 tools./updater. 前缀下。"""
    if isinstance(node, dict):
        for key, value in node.items():
            walk_owned(value, f"{prefix}.{key}" if prefix else str(key))
        return
    if prefix.split(".", 1)[0] not in OWNED_PREFIXES:
        raise SystemExit(f"fragment 含非 tools./updater./app. 前缀的键：{prefix}")


def deep_merge(target: dict, patch: dict, prefix: str = "") -> None:
    for key, value in patch.items():
        here = f"{prefix}.{key}" if prefix else str(key)
        if isinstance(value, dict):
            existing = target.get(key)
            if existing is None:
                target[key] = {}
            elif not isinstance(existing, dict):
                raise SystemExit(f"语言文件里 {here} 已是字符串，fragment 却要改成对象")
            deep_merge(target[key], value, here)
            continue
        target[key] = value


def write_lang(path: Path, data: dict) -> None:
    text = json.dumps(data, ensure_ascii=False, indent=2)
    payload = (text + "\n").replace("\n", "\r\n")
    path.write_bytes(payload.encode("utf-8"))


def check() -> int:
    catalogs = {lang: flatten(load_json(FRAG_DIR / f"frag.{lang}.json")) for lang in LANGUAGES}
    baseline = catalogs["zh-CN"]
    failed = False
    for lang in LANGUAGES:
        missing = sorted(set(baseline) - set(catalogs[lang]))
        extra = sorted(set(catalogs[lang]) - set(baseline))
        if missing or extra:
            failed = True
            print(f"[{lang}] 键集合不一致：缺 {missing}，多 {extra}")
    for key, zh in baseline.items():
        wanted = sorted(PLACEHOLDER.findall(zh))
        for lang in LANGUAGES:
            got = sorted(PLACEHOLDER.findall(catalogs[lang].get(key, "")))
            if got != wanted:
                failed = True
                print(f"[{lang}] {key} 占位符不一致：期望 {wanted}，实际 {got}")
    total = len(baseline)
    print(f"fragment 键数：{total}；校验{'失败' if failed else '通过'}")
    return 1 if failed else 0


def merge() -> int:
    failed = False
    for lang in LANGUAGES:
        fragment = load_json(FRAG_DIR / f"frag.{lang}.json")
        walk_owned(fragment)
        path = LANG_DIR / f"{lang}.json"
        catalog = load_json(path)
        before = flatten(catalog)
        deep_merge(catalog, fragment)
        after = flatten(catalog)
        added = sorted(set(after) - set(before))
        changed = sorted(key for key in set(after) & set(before) if after[key] != before[key])
        unrelated = [key for key in added if key.split(".", 1)[0] not in OWNED_PREFIXES]
        if unrelated:
            raise SystemExit(f"{lang}: 新键越界：{unrelated}")
        write_lang(path, catalog)
        print(f"{lang}: 新增 {len(added)}，更新 {len(changed)}，总键数 {len(after)}")
    return 1 if failed else 0


if __name__ == "__main__":
    if "--check" in sys.argv:
        sys.exit(check())
    sys.exit(merge())
