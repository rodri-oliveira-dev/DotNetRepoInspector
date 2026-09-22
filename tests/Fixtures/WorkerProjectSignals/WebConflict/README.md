# Web / Worker conflict

This fixture intentionally combines the authoritative Web SDK with the official evaluated Worker flag `UsingMicrosoftNETSdkWorker=true`.

Issue #47 treats this as conflicting strong workload evidence. After #152, the conservative result should be `unknown` unless a higher-precedence Test signal is present. The fixture prevents implementation order from silently choosing Web or Worker.
