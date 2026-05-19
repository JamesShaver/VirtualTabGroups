# VirtualTabGroups

## Building

Requires Visual Studio 2022 Build Tools with:
- Workload: "Desktop development with C++" (for the C++/CLI loader)
- Individual component: ".NET Framework 4.8 SDK" and ".NET Framework 4.8 targeting pack"

On machines without the targeting pack, `mscoree.lib` is regenerated at build time from the committed `mscoree.def`.
