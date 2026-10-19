"""AtomNES 发布公共逻辑。

默认：自包含 + 裁剪 + 单文件（可在 Windows/Linux 上为任意目标平台交叉发布）。
--aot：NativeAOT 发布（不支持跨系统，需在目标系统上运行；Linux 产物依赖构建机的 glibc 版本）。
"""
import argparse
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PROJECT = ROOT / "AtomNES" / "AtomNES.csproj"
OUTPUT = ROOT / "Output"


def publish(rid: str, aot_platform: str) -> int:
    parser = argparse.ArgumentParser(description=f"AtomNES 发布 ({rid})")
    parser.add_argument("--aot", action="store_true", help="使用 NativeAOT 发布")
    parser.add_argument("--keep-pdb", action="store_true", help="保留调试符号文件")
    args = parser.parse_args()

    if args.aot and not sys.platform.startswith(aot_platform):
        print(f"错误：NativeAOT 不支持跨系统编译，{rid} 的 AOT 发布必须在 {aot_platform} 上运行。")
        return 1
    if shutil.which("dotnet") is None:
        print("错误：未找到 dotnet，请安装 .NET 10 SDK。")
        return 1

    out_dir = OUTPUT / (f"{rid}-aot" if args.aot else rid)
    if out_dir.exists():
        shutil.rmtree(out_dir)

    cmd = ["dotnet", "publish", str(PROJECT), "-c", "Release", "-r", rid, "-o", str(out_dir)]
    if args.aot:
        cmd.append("-p:PublishAot=true")
    else:
        cmd += ["--self-contained", "true", "-p:PublishTrimmed=true", "-p:PublishSingleFile=true"]
    print(">", " ".join(cmd))
    code = subprocess.call(cmd, cwd=ROOT)
    if code != 0:
        print(f"发布失败，退出码 {code}")
        return code

    if not args.keep_pdb:
        for f in list(out_dir.glob("*.pdb")) + list(out_dir.glob("*.dbg")):
            f.unlink()

    total = 0
    print(f"\n发布完成：{out_dir}")
    for f in sorted(out_dir.iterdir()):
        if f.is_file():
            size = f.stat().st_size
            total += size
            print(f"  {f.name:<28}{size / 1048576:8.2f} MB")
    print(f"  {'合计':<26}{total / 1048576:8.2f} MB")
    return 0
