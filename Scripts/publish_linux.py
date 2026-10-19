"""发布 AtomNES (linux-x64) 到 Output/linux-x64。

默认自包含裁剪发布（可在 Windows 上交叉发布）；
加 --aot 使用 NativeAOT（需在 Linux 上运行，依赖 clang 与 zlib1g-dev，
产物要求运行机 glibc 版本不低于构建机）。
"""
import sys

from publish_common import publish

if __name__ == "__main__":
    sys.exit(publish("linux-x64", "linux"))
