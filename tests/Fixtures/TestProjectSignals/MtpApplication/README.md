# MTP test application signal

This fixture models the effective MSBuild shape of a Microsoft.Testing.Platform test application when `IsTestProject` is not true.

The authoritative research signal is the evaluated `IsTestingPlatformApplication=true` property. The property is declared directly so the fixture remains hermetic and does not require an external package restore merely to prove the evaluated classification fact.

Before issue #151, the production classifier does not consume this property and therefore sees only `OutputType=Exe`, reproducing the current false negative as `console`.
