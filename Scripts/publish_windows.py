"""发布 AtomNES (win-x64) 到 Output/win-x64。

默认自包含裁剪发布；加 --aot 使用 NativeAOT（需在 Windows 上运行）。
"""
import sys

from publish_common import publish

if __name__ == "__main__":
    sys.exit(publish("win-x64", "win"))
