# -*- coding: utf-8 -*-
"""打包腳本 — 使用 PyInstaller 將 app.py 打包為單一 exe"""
import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

APP_EXE_NAME = "零零快捷剪貼板.exe"
DIST_EXE_PATH = Path("dist") / APP_EXE_NAME


def run_pyinstaller():
    cmd = [
        sys.executable, "-m", "PyInstaller",
        "--noconfirm",
        "--onefile",
        "--windowed",
        "--noupx",
        "--name", "零零快捷剪貼板",
        "--icon", "assets/00monstericon.ico",
        "--version-file", "version_info.txt",
        "--add-data", "assets;assets",
        "--hidden-import", "pystray",
        "--hidden-import", "PIL",
        "--hidden-import", "PIL._tkinter_finder",
        "--collect-all", "customtkinter",
        "app.py",
    ]
    print("正在打包... 請稍候")
    print(" ".join(cmd))
    subprocess.run(cmd, check=True)


def resolve_signtool_path():
    custom_path = os.environ.get("SIGNTOOL_PATH", "").strip()
    if custom_path:
        return custom_path
    found = shutil.which("signtool")
    return found or ""


def sign_exe(exe_path: Path, required: bool = True):
    signtool = resolve_signtool_path()
    if not signtool:
        message = (
            "找不到 signtool。請安裝 Windows SDK，或設定 SIGNTOOL_PATH 指向 signtool.exe"
        )
        if required:
            raise RuntimeError(message)
        print(f"⚠️ {message}，略過簽章。")
        return False

    pfx_path = os.environ.get("SIGN_PFX_PATH", "").strip()
    pfx_password = os.environ.get("SIGN_PFX_PASSWORD", "").strip()
    cert_subject = os.environ.get("SIGN_CERT_SUBJECT", "").strip()
    cert_sha1 = os.environ.get("SIGN_CERT_SHA1", "").replace(" ", "").strip()
    timestamp_url = os.environ.get("SIGN_TIMESTAMP_URL", "http://timestamp.digicert.com").strip()

    cmd = [
        signtool,
        "sign",
        "/fd",
        "SHA256",
        "/td",
        "SHA256",
        "/tr",
        timestamp_url,
    ]

    if pfx_path:
        cmd.extend(["/f", pfx_path])
        if pfx_password:
            cmd.extend(["/p", pfx_password])
    elif cert_sha1:
        cmd.extend(["/sha1", cert_sha1])
    elif cert_subject:
        cmd.extend(["/n", cert_subject])
    else:
        message = (
            "未提供簽章憑證。請設定 SIGN_PFX_PATH（可搭配 SIGN_PFX_PASSWORD），"
            "或設定 SIGN_CERT_SHA1 / SIGN_CERT_SUBJECT。"
        )
        if required:
            raise RuntimeError(message)
        print(f"⚠️ {message} 略過簽章。")
        return False

    cmd.append(str(exe_path))

    print("\n正在進行程式碼簽章...")
    print(" ".join(cmd[:-1] + [APP_EXE_NAME]))
    subprocess.run(cmd, check=True)

    verify_cmd = [signtool, "verify", "/pa", "/v", str(exe_path)]
    print("\n正在驗證簽章...")
    subprocess.run(verify_cmd, check=True)
    print("✅ 簽章與驗證完成")
    return True


def parse_args():
    parser = argparse.ArgumentParser(description="打包並簽章 Windows EXE")
    parser.add_argument(
        "--unsigned",
        action="store_true",
        help="只打包不簽章（僅供本機開發測試，不建議發佈使用）",
    )
    return parser.parse_args()


def main():
    args = parse_args()
    run_pyinstaller()

    if not DIST_EXE_PATH.exists():
        raise FileNotFoundError(f"找不到輸出檔案：{DIST_EXE_PATH}")

    if args.unsigned:
        print("\n⚠️ 目前為 --unsigned 模式，已略過簽章。")
    else:
        sign_exe(DIST_EXE_PATH, required=True)

    print("\n✅ 打包完成！exe 檔案位於 dist/ 資料夾中")

if __name__ == "__main__":
    main()
