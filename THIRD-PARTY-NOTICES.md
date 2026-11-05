# Third-party notices

MERQO VERTEXA is built on the .NET platform and bundles a small number of open-source packages.
No telemetry, analytics or advertising SDK is included. The application contains no network client code.

## Runtime dependencies

| Package | Version | Licence | Used for |
| --- | --- | --- | --- |
| [.NET Runtime](https://github.com/dotnet/runtime) | 8.0.x | MIT | Base class library and Windows Desktop (WPF) runtime, bundled in the self-contained executable |
| [System.Management](https://www.nuget.org/packages/System.Management) | 8.0.0 | MIT | WMI/CIM access (`System.Management` managed wrapper over the Windows WMI service) |
| [Microsoft.Data.Sqlite](https://www.nuget.org/packages/Microsoft.Data.Sqlite) | 8.0.11 | MIT | Local settings and diagnostic-history database (`SQLitePCLRaw` / SQLite, public domain) |

## Build and test dependencies

| Package | Version | Licence |
| --- | --- | --- |
| [xunit](https://xunit.net/) | 2.9.2 | Apache-2.0 |
| [xunit.runner.visualstudio](https://github.com/xunit/visualstudio.xunit) | 2.8.2 | Apache-2.0 |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) | 17.11.1 | MIT |

## Design assets

The MERQO VERTEXA application icon (`assets/icons/`) is an original work created for this project with
generative image tooling and post-processed into the shipped multi-resolution `.ico`. It is not derived
from, and does not include, any MERQO brand logo, trademark, or third-party icon set. Segoe UI Variable and
Cascadia Mono are Windows system fonts and are neither bundled nor redistributed by this application.

## Full licence texts

MIT: <https://opensource.org/license/mit>
Apache-2.0: <https://www.apache.org/licenses/LICENSE-2.0>
SQLite (public domain): <https://www.sqlite.org/copyright.html>
