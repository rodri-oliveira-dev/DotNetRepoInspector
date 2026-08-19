# Missing SDK compatibility fixture

This synthetic repository requests an intentionally unavailable SDK (`999.0.100`) with roll-forward disabled.

The cross-platform compatibility gate verifies that SDK resolution failure remains stable across operating systems and is exposed through the public `DRI1002` (`DotNetSdkUnavailable`) error diagnostic rather than platform-specific text parsing.
