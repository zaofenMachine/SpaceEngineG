# 独立程序隐藏窗口验证失败

首个 Windows 包在隐藏窗口下能推进完整航线并达到地表，但六次 `ScreenCapture.CaptureScreenshot` 均报告 `Failed to capture screen shot`，所以验证器将本轮判定为失败。

该报告作为边界证据保留；后续必须在真实可见程序窗口下重新执行，并核对实际保存的截图和退出状态。不能把这一轮当作图形验证通过。
