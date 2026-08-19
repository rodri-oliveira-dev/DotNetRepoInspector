# Path casing compatibility fixture

This synthetic repository contains a project under a mixed-case directory and uses an uppercase `.CSPROJ` extension.

The compatibility gate verifies that discovery remains extension-case tolerant, public repository-relative paths preserve the filesystem casing, and JSON paths use `/` separators on every supported host operating system.
