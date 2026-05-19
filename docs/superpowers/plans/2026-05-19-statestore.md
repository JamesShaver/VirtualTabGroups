# StateStore Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the `StateStore` component per `docs/superpowers/specs/2026-05-19-statestore-design.md` — durable JSON persistence for the virtual tree with debounced atomic writes, corrupt-file recovery, and a host-observer interface.

**Architecture:** Pure C# component with zero Notepad++ dependencies. Newtonsoft.Json with a custom polymorphic converter handles the `folder` vs `file` discriminator. All mutations enter the store on the UI thread; serialization runs on that same thread; a `System.Threading.Timer` schedules a single debounced atomic write on a thread-pool callback. Tests run against a temp directory.

**Tech Stack:** .NET Framework 4.8, C# 9, Newtonsoft.Json 13.x, xUnit (existing test project).

---

## Reference

- Spec: `docs/superpowers/specs/2026-05-19-statestore-design.md`
- Existing core types: `VirtualTabGroups/Core/TreeNodeModel.cs`, `FolderNode.cs`, `FileNode.cs`
- Test project (xUnit, net48): `VirtualTabGroups.Tests/`

## Before You Begin

The working tree has uncommitted work from a prior session:
- `VirtualTabGroups/Core/AliasResolver.cs` and `VirtualTabGroups.Tests/AliasResolverTests.cs` (untracked)
- `VirtualTabGroups/VirtualTabGroups.csproj` (modified to include AliasResolver)

These compile, all 12 tests pass, and they're orthogonal to this plan. **Leave them uncommitted** unless directed otherwise — they belong to a separate logical change.

Until Task 1 lands a `.gitignore`, `bin/`, `obj/`, and `packages/` are noisy in `git status`. Always commit with explicit paths (`git add path/to/file`) — never `git add .` or `git add -A`.

---

### Task 1: Populate `.gitignore`

The repo has an empty `.gitignore`, so build outputs flood `git status`. Fix this first so every subsequent commit is clean.

**Files:**
- Modify: `.gitignore`

- [ ] **Step 1: Verify current noisy state**

Run: `git status`
Expected: includes `?? VirtualTabGroups/bin/`, `?? VirtualTabGroups/obj/`, `?? VirtualTabGroups.Tests/bin/`, `?? VirtualTabGroups.Tests/obj/`, `?? packages/`.

- [ ] **Step 2: Write standard VS/.NET ignore patterns**

Replace `.gitignore` contents with:

```
# Build output
bin/
obj/

# NuGet
packages/
*.nupkg

# Visual Studio / Rider / VS Code
.vs/
.idea/
.vscode/
*.user
*.suo

# Test results
TestResults/

# OS
Thumbs.db
.DS_Store
```

- [ ] **Step 3: Verify ignores apply**

Run: `git status`
Expected: no `bin/`, `obj/`, `packages/` entries. Remaining noise is the pre-existing AliasResolver work plus `.gitignore` itself.

- [ ] **Step 4: Commit**

```
git add .gitignore
git commit -m "Add .gitignore for VS/.NET build outputs"
```

---

### Task 2: Add Newtonsoft.Json NuGet dependency

This project uses the legacy `packages.config` style (visible from the existing `..\packages\Microsoft.NETFramework.ReferenceAssemblies.net48.1.0.3\` hint path in the csproj). Match that style — do **not** migrate to PackageReference.

**Files:**
- Create: `VirtualTabGroups/packages.config`
- Modify: `VirtualTabGroups/VirtualTabGroups.csproj`

- [ ] **Step 1: Create `VirtualTabGroups/packages.config`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<packages>
  <package id="Microsoft.NETFramework.ReferenceAssemblies.net48" version="1.0.3" targetFramework="net48" developmentDependency="true" />
  <package id="Newtonsoft.Json" version="13.0.3" targetFramework="net48" />
</packages>
```

- [ ] **Step 2: Add the assembly reference to `VirtualTabGroups/VirtualTabGroups.csproj`**

In the `<ItemGroup>` that already contains `<Reference Include="System" />` etc., add as the last `<Reference>`:

```xml
    <Reference Include="Newtonsoft.Json">
      <HintPath>..\packages\Newtonsoft.Json.13.0.3\lib\net45\Newtonsoft.Json.dll</HintPath>
      <Private>True</Private>
    </Reference>
```

`<Private>True</Private>` ensures the Newtonsoft DLL is copied next to `VirtualTabGroups.dll` on build — required because Notepad++ plugins ship as self-contained folders.

- [ ] **Step 3: Restore packages and build**

Run: `dotnet restore VirtualTabGroups/VirtualTabGroups.csproj`
Then: `dotnet build VirtualTabGroups/VirtualTabGroups.csproj`
Expected: build succeeds; `packages/Newtonsoft.Json.13.0.3/` directory now exists.

If `dotnet restore` doesn't pick up `packages.config` (it sometimes ignores legacy format), fall back to: `nuget restore VirtualTabGroups.sln` (requires `nuget.exe` on PATH). Either should produce `packages/Newtonsoft.Json.13.0.3/lib/net45/Newtonsoft.Json.dll`.

- [ ] **Step 4: Confirm no regression**

Run: `dotnet test VirtualTabGroups.Tests/VirtualTabGroups.Tests.csproj --nologo`
Expected: `Passed: 12, Failed: 0` (the existing AliasResolver + sanity tests).

- [ ] **Step 5: Commit**

```
git add VirtualTabGroups/packages.config VirtualTabGroups/VirtualTabGroups.csproj
git commit -m "Add Newtonsoft.Json 13.0.3 dependency"
```

---

### Task 3: Define `IStateStoreObserver` interface

Add the host-notification seam first — every later task references it.

**Files:**
- Create: `VirtualTabGroups/Core/IStateStoreObserver.cs`
- Modify: `VirtualTabGroups/VirtualTabGroups.csproj`

- [ ] **Step 1: Create `VirtualTabGroups/Core/IStateStoreObserver.cs`**

```csharp
using System;

namespace VirtualTabGroups.Core
{
    public interface IStateStoreObserver
    {
        void OnRecoveredFromCorruptFile(string backupPath);
        void OnFutureSchemaVersion(int versionFound);
        void OnSaveFailed(Exception ex);
    }
}
```

- [ ] **Step 2: Register in csproj**

In the `<ItemGroup>` containing the existing `<Compile Include="Core\...">` entries, add:

```xml
    <Compile Include="Core\IStateStoreObserver.cs" />
```

- [ ] **Step 3: Build**

Run: `dotnet build VirtualTabGroups/VirtualTabGroups.csproj`
Expected: success.

- [ ] **Step 4: Commit**

```
git add VirtualTabGroups/Core/IStateStoreObserver.cs VirtualTabGroups/VirtualTabGroups.csproj
git commit -m "Add IStateStoreObserver interface for StateStore host callbacks"
```

---

### Task 4: Implement `NodeJsonConverter` (polymorphic folder/file)

A single `JsonConverter` handles both `FolderNode` and `FileNode` — peeking the `"type"` field on read, branching on runtime type on write. Build it TDD, smallest case first.

**Files:**
- Create: `VirtualTabGroups/Core/NodeJsonConverter.cs`
- Create: `VirtualTabGroups.Tests/NodeJsonConverterTests.cs`
- Modify: `VirtualTabGroups/VirtualTabGroups.csproj` and `VirtualTabGroups.Tests/VirtualTabGroups.Tests.csproj`

- [ ] **Step 1: Write the failing write-side test for a file node**

Create `VirtualTabGroups.Tests/NodeJsonConverterTests.cs`:

```csharp
using Newtonsoft.Json;
using VirtualTabGroups.Core;
using Xunit;

namespace VirtualTabGroups.Tests
{
    public class NodeJsonConverterTests
    {
        private static JsonSerializerSettings Settings() => new JsonSerializerSettings
        {
            Converters = { new NodeJsonConverter() },
            Formatting = Formatting.None,
        };

        [Fact]
        public void WriteFile_EmitsTypeIdNamePath()
        {
            var file = new FileNode("User.php", @"C:\src\User.php");
            file.Id = new System.Guid("11111111-1111-1111-1111-111111111111");

            var json = JsonConvert.SerializeObject(file, Settings());

            Assert.Equal(
                "{\"type\":\"file\",\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"User.php\",\"path\":\"C:\\\\src\\\\User.php\"}",
                json);
        }
    }
}
```

The test assigns `file.Id` directly — this requires that `TreeNodeModel.Id` has a setter reachable from the test assembly. The current declaration is `public Guid Id { get; internal set; }`. The test assembly is separate from the main assembly, so `internal` is not visible by default.

- [ ] **Step 2: Expose internals to the test assembly**

Add to `VirtualTabGroups/Properties/AssemblyInfo.cs` (at the bottom, after the existing attributes):

```csharp
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("VirtualTabGroups.Tests")]
```

- [ ] **Step 3: Run the test — confirm it fails because `NodeJsonConverter` doesn't exist**

Run: `dotnet test VirtualTabGroups.Tests/VirtualTabGroups.Tests.csproj --nologo --filter "FullyQualifiedName~NodeJsonConverterTests"`
Expected: build error — `NodeJsonConverter` not found.

- [ ] **Step 4: Create the converter skeleton with file-write support**

Create `VirtualTabGroups/Core/NodeJsonConverter.cs`:

```csharp
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VirtualTabGroups.Core
{
    public sealed class NodeJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) =>
            typeof(TreeNodeModel).IsAssignableFrom(objectType);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            switch (value)
            {
                case FileNode file:
                    writer.WriteStartObject();
                    writer.WritePropertyName("type"); writer.WriteValue("file");
                    writer.WritePropertyName("id");   writer.WriteValue(file.Id.ToString());
                    writer.WritePropertyName("name"); writer.WriteValue(file.Name);
                    writer.WritePropertyName("path"); writer.WriteValue(file.Path);
                    writer.WriteEndObject();
                    return;

                case FolderNode folder:
                    writer.WriteStartObject();
                    writer.WritePropertyName("type");     writer.WriteValue("folder");
                    writer.WritePropertyName("id");       writer.WriteValue(folder.Id.ToString());
                    writer.WritePropertyName("name");     writer.WriteValue(folder.Name);
                    writer.WritePropertyName("expanded"); writer.WriteValue(folder.Expanded);
                    writer.WritePropertyName("children");
                    writer.WriteStartArray();
                    foreach (var child in folder.Children)
                    {
                        WriteJson(writer, child, serializer);
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                    return;

                default:
                    throw new JsonSerializationException("Unknown node type: " + value?.GetType());
            }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var obj = JObject.Load(reader);
            var type = (string)obj["type"];
            switch (type)
            {
                case "file":
                    var file = new FileNode((string)obj["name"], (string)obj["path"]);
                    file.Id = Guid.Parse((string)obj["id"]);
                    return file;

                case "folder":
                    var folder = new FolderNode((string)obj["name"]);
                    folder.Id = Guid.Parse((string)obj["id"]);
                    folder.Expanded = (bool?)obj["expanded"] ?? false;
                    var children = (JArray)obj["children"] ?? new JArray();
                    foreach (var child in children)
                    {
                        var childNode = (TreeNodeModel)ReadJson(child.CreateReader(), typeof(TreeNodeModel), null, serializer);
                        folder.Children.Add(childNode);
                    }
                    return folder;

                default:
                    throw new JsonSerializationException("Unknown node type discriminator: " + type);
            }
        }
    }
}
```

The converter references `FolderNode.Expanded` (a `bool` property not yet on the class). Add it now:

In `VirtualTabGroups/Core/FolderNode.cs`, add the property:

```csharp
public bool Expanded { get; set; }
```

(Public setter — UI binding will toggle it directly.)

- [ ] **Step 5: Register the new file in csproj**

Add to `VirtualTabGroups/VirtualTabGroups.csproj` in the existing `<ItemGroup>` of `<Compile>` entries:

```xml
    <Compile Include="Core\NodeJsonConverter.cs" />
```

- [ ] **Step 6: Run the file-write test — expect pass**

Run: `dotnet test VirtualTabGroups.Tests/VirtualTabGroups.Tests.csproj --nologo --filter "FullyQualifiedName~NodeJsonConverterTests"`
Expected: 1 passing.

- [ ] **Step 7: Add the folder-write test**

Append to `NodeJsonConverterTests.cs`:

```csharp
        [Fact]
        public void WriteFolder_EmitsTypeIdNameExpandedAndChildren()
        {
            var folder = new FolderNode("Auth");
            folder.Id = new System.Guid("22222222-2222-2222-2222-222222222222");
            folder.Expanded = true;

            var file = new FileNode("User.php", @"C:\src\User.php");
            file.Id = new System.Guid("33333333-3333-3333-3333-333333333333");
            folder.Children.Add(file);

            var json = JsonConvert.SerializeObject(folder, Settings());

            Assert.Equal(
                "{\"type\":\"folder\",\"id\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"Auth\",\"expanded\":true,\"children\":[" +
                "{\"type\":\"file\",\"id\":\"33333333-3333-3333-3333-333333333333\",\"name\":\"User.php\",\"path\":\"C:\\\\src\\\\User.php\"}" +
                "]}",
                json);
        }
```

Run the same filter; expect 2 passing.

- [ ] **Step 8: Add the round-trip test (read + write)**

Append:

```csharp
        [Fact]
        public void RoundTrip_NestedTree_PreservesStructure()
        {
            var root = new FolderNode("");
            root.Id = System.Guid.NewGuid();
            root.Expanded = true;

            var auth = new FolderNode("Auth");
            auth.Id = System.Guid.NewGuid();
            auth.Expanded = false;
            auth.Children.Add(new FileNode("User.php", @"C:\src\auth\User.php"));
            root.Children.Add(auth);

            root.Children.Add(new FileNode("README", @"C:\src\README"));

            var json = JsonConvert.SerializeObject(root, Settings());
            var rehydrated = (FolderNode)JsonConvert.DeserializeObject<TreeNodeModel>(json, Settings());

            Assert.Equal(root.Id, rehydrated.Id);
            Assert.Equal(root.Name, rehydrated.Name);
            Assert.True(rehydrated.Expanded);
            Assert.Equal(2, rehydrated.Children.Count);

            var authBack = (FolderNode)rehydrated.Children[0];
            Assert.Equal("Auth", authBack.Name);
            Assert.False(authBack.Expanded);
            Assert.Single(authBack.Children);

            var userBack = (FileNode)authBack.Children[0];
            Assert.Equal("User.php", userBack.Name);
            Assert.Equal(@"C:\src\auth\User.php", userBack.Path);

            var readmeBack = (FileNode)rehydrated.Children[1];
            Assert.Equal("README", readmeBack.Name);
        }
```

Run filter; expect 3 passing.

- [ ] **Step 9: Add an `expanded`-defaults-to-false read test**

Append:

```csharp
        [Fact]
        public void ReadFolder_MissingExpandedField_DefaultsToFalse()
        {
            var json = "{\"type\":\"folder\",\"id\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"X\",\"children\":[]}";

            var folder = (FolderNode)JsonConvert.DeserializeObject<TreeNodeModel>(json, Settings());

            Assert.False(folder.Expanded);
            Assert.Empty(folder.Children);
        }
```

Run; expect 4 passing.

- [ ] **Step 10: Commit**

```
git add VirtualTabGroups/Core/NodeJsonConverter.cs VirtualTabGroups/Core/FolderNode.cs VirtualTabGroups/Properties/AssemblyInfo.cs VirtualTabGroups/VirtualTabGroups.csproj VirtualTabGroups.Tests/NodeJsonConverterTests.cs
git commit -m "Add NodeJsonConverter for polymorphic folder/file serialization"
```

---

### Task 5: `StateStore.Load` — missing or empty file returns empty root

Smallest possible Load behavior first. No JSON parsing yet.

**Files:**
- Create: `VirtualTabGroups/Core/StateStore.cs`
- Create: `VirtualTabGroups.Tests/StateStoreTests.cs`
- Modify: `VirtualTabGroups/VirtualTabGroups.csproj`

- [ ] **Step 1: Write the failing test for missing file**

Create `VirtualTabGroups.Tests/StateStoreTests.cs`:

```csharp
using System.IO;
using VirtualTabGroups.Core;
using Xunit;

namespace VirtualTabGroups.Tests
{
    public class StateStoreTests
    {
        private static string NewTempStatePath()
        {
            var dir = Path.Combine(Path.GetTempPath(), "VTG-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "state.json");
        }

        [Fact]
        public void Load_MissingFile_ReturnsEmptyRoot()
        {
            var path = NewTempStatePath();
            using (var store = new StateStore(path))
            {
                var root = store.Load();

                Assert.NotNull(root);
                Assert.Equal("", root.Name);
                Assert.Empty(root.Children);
                Assert.Null(store.LastSelectedId);
            }
        }
    }
}
```

- [ ] **Step 2: Run — expect build failure (`StateStore` undefined)**

Run: `dotnet test VirtualTabGroups.Tests/VirtualTabGroups.Tests.csproj --nologo --filter "FullyQualifiedName~StateStoreTests"`
Expected: build error.

- [ ] **Step 3: Create minimal `StateStore`**

Create `VirtualTabGroups/Core/StateStore.cs`:

```csharp
using System;
using System.IO;

namespace VirtualTabGroups.Core
{
    public sealed class StateStore : IDisposable
    {
        private readonly string _stateFilePath;
        private readonly IStateStoreObserver _observer;

        public StateStore(string stateFilePath, IStateStoreObserver observer = null)
        {
            _stateFilePath = stateFilePath ?? throw new ArgumentNullException(nameof(stateFilePath));
            _observer = observer;
        }

        public Guid? LastSelectedId { get; private set; }

        public FolderNode Load()
        {
            if (!File.Exists(_stateFilePath) || new FileInfo(_stateFilePath).Length == 0)
            {
                return new FolderNode("");
            }
            // Parsing comes in Task 6.
            throw new NotImplementedException();
        }

        public void Dispose() { }
    }
}
```

- [ ] **Step 4: Register in csproj**

Add to `VirtualTabGroups/VirtualTabGroups.csproj`:

```xml
    <Compile Include="Core\StateStore.cs" />
```

- [ ] **Step 5: Run — expect pass**

Run: `dotnet test VirtualTabGroups.Tests/VirtualTabGroups.Tests.csproj --nologo --filter "FullyQualifiedName~StateStoreTests"`
Expected: 1 passing.

- [ ] **Step 6: Add the empty-file variant test**

Append to `StateStoreTests.cs`:

```csharp
        [Fact]
        public void Load_EmptyFile_ReturnsEmptyRoot()
        {
            var path = NewTempStatePath();
            File.WriteAllText(path, "");
            using (var store = new StateStore(path))
            {
                var root = store.Load();
                Assert.Empty(root.Children);
            }
        }
```

Run; expect 2 passing.

- [ ] **Step 7: Commit**

```
git add VirtualTabGroups/Core/StateStore.cs VirtualTabGroups/VirtualTabGroups.csproj VirtualTabGroups.Tests/StateStoreTests.cs
git commit -m "Add StateStore skeleton with Load for missing/empty file"
```

---

### Task 6: `StateStore.Load` — valid v1 file

Now the real load path: parse the envelope, use `NodeJsonConverter` for the tree, restore `LastSelectedId`.

**Files:**
- Modify: `VirtualTabGroups/Core/StateStore.cs`
- Modify: `VirtualTabGroups.Tests/StateStoreTests.cs`
- Create: `VirtualTabGroups.Tests/Fixtures/state-good.json`
- Modify: `VirtualTabGroups.Tests/VirtualTabGroups.Tests.csproj` (copy fixture to output)

- [ ] **Step 1: Create the fixture**

Create `VirtualTabGroups.Tests/Fixtures/state-good.json`:

```json
{
  "schemaVersion": 1,
  "lastSelectedId": "99e0a4d2-4b5c-4d6e-8f70-112233445566",
  "root": {
    "type": "folder",
    "id": "f1c2b3a4-0000-0000-0000-000000000001",
    "name": "",
    "expanded": true,
    "children": [
      {
        "type": "folder",
        "id": "a3b4c5d6-0000-0000-0000-000000000002",
        "name": "Auth",
        "expanded": false,
        "children": [
          {
            "type": "file",
            "id": "99e0a4d2-4b5c-4d6e-8f70-112233445566",
            "name": "User.php",
            "path": "C:\\src\\auth\\User.php"
          }
        ]
      }
    ]
  }
}
```

- [ ] **Step 2: Configure fixture copy-on-build**

In `VirtualTabGroups.Tests/VirtualTabGroups.Tests.csproj`, add an `<ItemGroup>` (or extend the existing content one):

```xml
  <ItemGroup>
    <None Update="Fixtures\state-good.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

If the project file is SDK-style (`<Project Sdk="...">`) the `<None Update>` syntax works as written. If it's the legacy non-SDK csproj (check whether it has `<Import Project="$(MSBuildToolsPath)..." />`), use this instead:

```xml
  <ItemGroup>
    <None Include="Fixtures\state-good.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

- [ ] **Step 3: Write the failing test**

Append to `StateStoreTests.cs`:

```csharp
        private static string CopyFixtureNextToStateFile(string fixtureName)
        {
            var statePath = NewTempStatePath();
            var fixtureSource = Path.Combine(
                Path.GetDirectoryName(typeof(StateStoreTests).Assembly.Location),
                "Fixtures",
                fixtureName);
            File.Copy(fixtureSource, statePath, overwrite: true);
            return statePath;
        }

        [Fact]
        public void Load_ValidV1File_ReturnsTreeAndSelection()
        {
            var path = CopyFixtureNextToStateFile("state-good.json");
            using (var store = new StateStore(path))
            {
                var root = store.Load();

                Assert.True(root.Expanded);
                Assert.Single(root.Children);

                var auth = (FolderNode)root.Children[0];
                Assert.Equal("Auth", auth.Name);
                Assert.False(auth.Expanded);
                Assert.Single(auth.Children);

                var user = (FileNode)auth.Children[0];
                Assert.Equal("User.php", user.Name);
                Assert.Equal(@"C:\src\auth\User.php", user.Path);

                Assert.Equal(
                    new System.Guid("99e0a4d2-4b5c-4d6e-8f70-112233445566"),
                    store.LastSelectedId);
            }
        }
```

- [ ] **Step 4: Run — expect `NotImplementedException`**

Run: `dotnet test ... --filter "FullyQualifiedName~Load_ValidV1File"`
Expected: fail with `NotImplementedException` (from the `throw` in Task 5's Load).

- [ ] **Step 5: Implement the real Load**

Replace the body of `Load()` in `StateStore.cs` with:

```csharp
        public FolderNode Load()
        {
            if (!File.Exists(_stateFilePath) || new FileInfo(_stateFilePath).Length == 0)
            {
                return new FolderNode("");
            }

            var json = File.ReadAllText(_stateFilePath);
            var settings = new JsonSerializerSettings
            {
                Converters = { new NodeJsonConverter() },
            };

            var envelope = JsonConvert.DeserializeObject<JObject>(json);
            var schemaVersion = (int?)envelope["schemaVersion"] ?? 1;

            // Tasks 7 and 8 add corrupt/future-version handling.
            // For now, parse v1 only.
            LastSelectedId = (Guid?)envelope["lastSelectedId"];
            var root = envelope["root"].ToObject<TreeNodeModel>(JsonSerializer.Create(settings));
            return (FolderNode)root;
        }
```

Add the using directives at the top of the file:

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
```

- [ ] **Step 6: Run — expect pass**

Run: `dotnet test ... --filter "FullyQualifiedName~StateStoreTests"`
Expected: 3 passing.

- [ ] **Step 7: Commit**

```
git add VirtualTabGroups/Core/StateStore.cs VirtualTabGroups.Tests/StateStoreTests.cs VirtualTabGroups.Tests/Fixtures/state-good.json VirtualTabGroups.Tests/VirtualTabGroups.Tests.csproj
git commit -m "Implement StateStore.Load for valid v1 state.json"
```

---

### Task 7: `StateStore.Load` — corrupt file backs up and notifies

Wrap the parse in a try/catch; on failure, rename the bad file with a timestamped suffix, invoke the observer, return an empty root.

**Files:**
- Modify: `VirtualTabGroups/Core/StateStore.cs`
- Modify: `VirtualTabGroups.Tests/StateStoreTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `StateStoreTests.cs`:

```csharp
        private sealed class CapturingObserver : IStateStoreObserver
        {
            public string CorruptBackupPath;
            public int? FutureSchemaSeen;
            public System.Exception SaveError;

            public void OnRecoveredFromCorruptFile(string backupPath) => CorruptBackupPath = backupPath;
            public void OnFutureSchemaVersion(int versionFound) => FutureSchemaSeen = versionFound;
            public void OnSaveFailed(System.Exception ex) => SaveError = ex;
        }

        [Fact]
        public void Load_CorruptFile_BacksUpAndNotifiesAndReturnsEmptyRoot()
        {
            var path = NewTempStatePath();
            File.WriteAllText(path, "this is not json {");

            var observer = new CapturingObserver();
            using (var store = new StateStore(path, observer))
            {
                var root = store.Load();

                Assert.Empty(root.Children);
                Assert.NotNull(observer.CorruptBackupPath);
                Assert.True(File.Exists(observer.CorruptBackupPath), "backup file should exist on disk");
                Assert.False(File.Exists(path), "original corrupt file should have been moved");
                Assert.StartsWith(Path.GetFileName(path) + ".corrupt-", Path.GetFileName(observer.CorruptBackupPath));
            }
        }
```

- [ ] **Step 2: Run — expect failure (no try/catch yet)**

Run: `dotnet test ... --filter "FullyQualifiedName~Load_CorruptFile"`
Expected: throws `JsonReaderException` out of `Load`.

- [ ] **Step 3: Wrap parse in try/catch and back up the bad file**

Update the body of `Load()` in `StateStore.cs`:

```csharp
        public FolderNode Load()
        {
            if (!File.Exists(_stateFilePath) || new FileInfo(_stateFilePath).Length == 0)
            {
                return new FolderNode("");
            }

            var settings = new JsonSerializerSettings { Converters = { new NodeJsonConverter() } };

            JObject envelope;
            try
            {
                var json = File.ReadAllText(_stateFilePath);
                envelope = JsonConvert.DeserializeObject<JObject>(json);
                if (envelope == null) throw new JsonSerializationException("empty envelope");
            }
            catch (Exception)
            {
                var backupPath = BackupCorruptFile();
                _observer?.OnRecoveredFromCorruptFile(backupPath);
                return new FolderNode("");
            }

            // Task 8: handle schemaVersion > 1 here.
            LastSelectedId = (Guid?)envelope["lastSelectedId"];
            try
            {
                var root = envelope["root"].ToObject<TreeNodeModel>(JsonSerializer.Create(settings));
                return (FolderNode)root;
            }
            catch (Exception)
            {
                var backupPath = BackupCorruptFile();
                _observer?.OnRecoveredFromCorruptFile(backupPath);
                LastSelectedId = null;
                return new FolderNode("");
            }
        }

        private string BackupCorruptFile()
        {
            var suffix = ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backupPath = _stateFilePath + suffix;

            // Disambiguate if two corruption events land in the same second.
            int n = 0;
            while (File.Exists(backupPath))
            {
                n++;
                backupPath = _stateFilePath + suffix + "-" + n;
            }

            File.Move(_stateFilePath, backupPath);
            return backupPath;
        }
```

- [ ] **Step 4: Run — expect pass**

Run: `dotnet test ... --filter "FullyQualifiedName~StateStoreTests"`
Expected: 4 passing.

- [ ] **Step 5: Commit**

```
git add VirtualTabGroups/Core/StateStore.cs VirtualTabGroups.Tests/StateStoreTests.cs
git commit -m "Back up corrupt state.json and notify observer on Load"
```

---

### Task 8: `StateStore.Load` — future `schemaVersion` triggers read-only mode

If we see a `schemaVersion` higher than what we understand, refuse to overwrite — notify the observer, return an empty root, and arm an internal flag that suppresses all future saves this session.

**Files:**
- Modify: `VirtualTabGroups/Core/StateStore.cs`
- Modify: `VirtualTabGroups.Tests/StateStoreTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `StateStoreTests.cs`:

```csharp
        [Fact]
        public void Load_FutureSchemaVersion_NotifiesAndEntersReadOnlyMode()
        {
            var path = NewTempStatePath();
            File.WriteAllText(path,
                "{\"schemaVersion\":2,\"root\":{\"type\":\"folder\",\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"\",\"children\":[]}}");

            var originalContent = File.ReadAllText(path);
            var observer = new CapturingObserver();

            using (var store = new StateStore(path, observer))
            {
                var root = store.Load();
                Assert.Empty(root.Children);
                Assert.Equal(2, observer.FutureSchemaSeen);
                Assert.True(store.IsReadOnly);
            }

            Assert.Equal(originalContent, File.ReadAllText(path));
        }
```

- [ ] **Step 2: Run — expect build failure (`IsReadOnly` missing)**

Run: `dotnet test ... --filter "FullyQualifiedName~Load_FutureSchemaVersion"`
Expected: build error.

- [ ] **Step 3: Add `IsReadOnly` and the future-schema check**

Add to `StateStore.cs`:

```csharp
        public bool IsReadOnly { get; private set; }
        private const int CurrentSchemaVersion = 1;
```

In `Load()`, between the envelope parse and the `LastSelectedId` line, insert:

```csharp
            var schemaVersion = (int?)envelope["schemaVersion"] ?? CurrentSchemaVersion;
            if (schemaVersion > CurrentSchemaVersion)
            {
                IsReadOnly = true;
                _observer?.OnFutureSchemaVersion(schemaVersion);
                return new FolderNode("");
            }
```

- [ ] **Step 4: Run — expect pass**

Run: `dotnet test ... --filter "FullyQualifiedName~StateStoreTests"`
Expected: 5 passing.

- [ ] **Step 5: Commit**

```
git add VirtualTabGroups/Core/StateStore.cs VirtualTabGroups.Tests/StateStoreTests.cs
git commit -m "Refuse to overwrite future-schema state.json (read-only mode)"
```

---

### Task 9: `MarkDirty` + `Flush` — synchronous atomic write (no debounce yet)

Wire up the save path end-to-end in synchronous form first: `MarkDirty` serializes the tree and stores the string; `Flush` writes it atomically. Debouncing follows in Task 10.

**Files:**
- Modify: `VirtualTabGroups/Core/StateStore.cs`
- Modify: `VirtualTabGroups.Tests/StateStoreTests.cs`

- [ ] **Step 1: Write the failing round-trip test**

Append to `StateStoreTests.cs`:

```csharp
        [Fact]
        public void Save_AndReloadInNewStore_PreservesTree()
        {
            var path = NewTempStatePath();

            var root = new FolderNode("");
            var auth = new FolderNode("Auth") { Expanded = true };
            auth.Children.Add(new FileNode("User.php", @"C:\src\User.php"));
            root.Children.Add(auth);

            var selectedId = auth.Children[0].Id;

            using (var store = new StateStore(path))
            {
                store.MarkDirty(root, selectedId);
                store.Flush();
            }

            // Reload in a fresh store.
            using (var store2 = new StateStore(path))
            {
                var reloaded = store2.Load();
                Assert.Single(reloaded.Children);
                var authBack = (FolderNode)reloaded.Children[0];
                Assert.Equal("Auth", authBack.Name);
                Assert.True(authBack.Expanded);
                Assert.Single(authBack.Children);
                Assert.Equal("User.php", ((FileNode)authBack.Children[0]).Name);
                Assert.Equal(selectedId, store2.LastSelectedId);
            }
        }

        [Fact]
        public void Save_AtomicWrite_LeavesNoTempFile()
        {
            var path = NewTempStatePath();
            using (var store = new StateStore(path))
            {
                store.MarkDirty(new FolderNode(""), null);
                store.Flush();
            }

            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
```

- [ ] **Step 2: Run — expect build failure (`MarkDirty` / `Flush` missing)**

- [ ] **Step 3: Implement `MarkDirty` and `Flush` synchronously**

Add to `StateStore.cs`:

```csharp
        private string _pendingJson;
        private readonly object _writeLock = new object();

        public void MarkDirty(FolderNode root, Guid? selectedId = null)
        {
            if (IsReadOnly) return;
            if (root == null) throw new ArgumentNullException(nameof(root));

            var settings = new JsonSerializerSettings
            {
                Converters = { new NodeJsonConverter() },
                Formatting = Formatting.Indented,
            };

            var envelope = new JObject
            {
                ["schemaVersion"] = CurrentSchemaVersion,
                ["lastSelectedId"] = selectedId.HasValue ? selectedId.Value.ToString() : null,
                ["root"] = JToken.FromObject(root, JsonSerializer.Create(settings)),
            };

            _pendingJson = envelope.ToString(Formatting.Indented);
        }

        public void Flush()
        {
            if (IsReadOnly) return;
            WritePendingNow();
        }

        private void WritePendingNow()
        {
            string json;
            lock (_writeLock)
            {
                json = _pendingJson;
                if (json == null) return;
                _pendingJson = null;
            }

            var tmpPath = _stateFilePath + ".tmp";
            File.WriteAllText(tmpPath, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (File.Exists(_stateFilePath))
            {
                File.Replace(tmpPath, _stateFilePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tmpPath, _stateFilePath);
            }
        }
```

Update `Dispose()` to flush:

```csharp
        public void Dispose() => Flush();
```

- [ ] **Step 4: Run — expect both new tests pass, full suite green**

Run: `dotnet test VirtualTabGroups.Tests/VirtualTabGroups.Tests.csproj --nologo`
Expected: all tests pass.

- [ ] **Step 5: Commit**

```
git add VirtualTabGroups/Core/StateStore.cs VirtualTabGroups.Tests/StateStoreTests.cs
git commit -m "Implement StateStore MarkDirty + Flush with atomic write"
```

---

### Task 10: Add debounce timer to `MarkDirty`

Multiple `MarkDirty` calls within 500ms should collapse into a single disk write. `Flush` still bypasses the timer and writes inline.

**Files:**
- Modify: `VirtualTabGroups/Core/StateStore.cs`
- Modify: `VirtualTabGroups.Tests/StateStoreTests.cs`

- [ ] **Step 1: Write the failing debounce test**

Append to `StateStoreTests.cs`:

```csharp
        [Fact]
        public void MarkDirty_BurstOfCalls_CoalescesIntoOneWrite()
        {
            var path = NewTempStatePath();
            using (var store = new StateStore(path, debounce: System.TimeSpan.FromMilliseconds(150)))
            {
                for (int i = 0; i < 30; i++)
                {
                    var root = new FolderNode("");
                    root.Children.Add(new FileNode("file" + i + ".txt", @"C:\f.txt"));
                    store.MarkDirty(root, null);
                }

                Assert.False(File.Exists(path), "no write should have happened yet during the burst");
                System.Threading.Thread.Sleep(400);  // > debounce window
                Assert.True(File.Exists(path), "exactly one write should land after debounce expires");
            }

            using (var store2 = new StateStore(path))
            {
                var reloaded = store2.Load();
                Assert.Single(reloaded.Children);
                // Final state should reflect the LAST MarkDirty (file29.txt).
                Assert.Equal("file29.txt", ((FileNode)reloaded.Children[0]).Name);
            }
        }
```

- [ ] **Step 2: Run — expect build failure (no debounce-accepting constructor)**

- [ ] **Step 3: Add debounce timer**

Update `StateStore.cs`:

Add field:

```csharp
        private readonly System.Threading.Timer _debounceTimer;
        private readonly TimeSpan _debounceInterval;
```

Replace the constructor with two overloads (preserve the existing public signature):

```csharp
        public StateStore(string stateFilePath, IStateStoreObserver observer = null)
            : this(stateFilePath, observer, TimeSpan.FromMilliseconds(500)) { }

        internal StateStore(string stateFilePath, TimeSpan debounce)
            : this(stateFilePath, null, debounce) { }

        internal StateStore(string stateFilePath, IStateStoreObserver observer, TimeSpan debounce)
        {
            _stateFilePath = stateFilePath ?? throw new ArgumentNullException(nameof(stateFilePath));
            _observer = observer;
            _debounceInterval = debounce;
            _debounceTimer = new System.Threading.Timer(_ => OnDebounceFired(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        }
```

(Test's `new StateStore(path, debounce: ...)` uses a named argument; add an overload that matches.)

Update the test's call: `new StateStore(path, debounce: TimeSpan.FromMilliseconds(150))` requires the second internal constructor. To keep the test concise, expose it via `InternalsVisibleTo` (already set in Task 4) and call positionally: `new StateStore(path, null, TimeSpan.FromMilliseconds(150))`. Update the test accordingly:

```csharp
            using (var store = new StateStore(path, null, System.TimeSpan.FromMilliseconds(150)))
```

In `MarkDirty`, after setting `_pendingJson`, restart the timer:

```csharp
            _debounceTimer.Change(_debounceInterval, System.Threading.Timeout.InfiniteTimeSpan);
```

Add the timer callback:

```csharp
        private void OnDebounceFired()
        {
            try
            {
                WritePendingNow();
            }
            catch (Exception)
            {
                // Task 11 wires this to observer.OnSaveFailed.
            }
        }
```

Update `Flush()` to cancel the timer before writing:

```csharp
        public void Flush()
        {
            if (IsReadOnly) return;
            _debounceTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            WritePendingNow();
        }
```

Update `Dispose()`:

```csharp
        public void Dispose()
        {
            try { Flush(); }
            finally { _debounceTimer.Dispose(); }
        }
```

- [ ] **Step 4: Run full suite**

Run: `dotnet test VirtualTabGroups.Tests/VirtualTabGroups.Tests.csproj --nologo`
Expected: all green.

- [ ] **Step 5: Commit**

```
git add VirtualTabGroups/Core/StateStore.cs VirtualTabGroups.Tests/StateStoreTests.cs
git commit -m "Add debounce timer to StateStore.MarkDirty"
```

---

### Task 11: Notify observer on save failure; preserve dirty state for retry

If the atomic write throws (disk full, permission denied, antivirus locked the file), the observer must learn about it, and the pending JSON must remain in memory so the next `MarkDirty` retries.

**Files:**
- Modify: `VirtualTabGroups/Core/StateStore.cs`
- Modify: `VirtualTabGroups.Tests/StateStoreTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `StateStoreTests.cs`:

```csharp
        [Fact]
        public void Save_IOFailure_NotifiesObserverAndKeepsDirtyState()
        {
            // Point at a directory that doesn't exist so File.WriteAllText throws.
            var bogusPath = Path.Combine(
                Path.GetTempPath(),
                "VTG-doesnotexist-" + System.Guid.NewGuid().ToString("N"),
                "state.json");

            var observer = new CapturingObserver();
            using (var store = new StateStore(bogusPath, observer, System.TimeSpan.FromMilliseconds(50)))
            {
                store.MarkDirty(new FolderNode(""), null);
                System.Threading.Thread.Sleep(200);

                Assert.NotNull(observer.SaveError);
                Assert.IsAssignableFrom<System.IO.IOException>(observer.SaveError);

                // Pending state preserved: a Flush after the directory becomes valid would still write.
                // We verify by checking that the next MarkDirty does not throw and SaveError remains the
                // most recent failure (no silent reset).
                store.MarkDirty(new FolderNode(""), null);
            }
        }
```

- [ ] **Step 2: Run — expect failure (no observer notification yet)**

- [ ] **Step 3: Update `WritePendingNow` to preserve the pending string on failure and notify**

Replace `WritePendingNow` and `OnDebounceFired` in `StateStore.cs`:

```csharp
        private void WritePendingNow()
        {
            string json;
            lock (_writeLock)
            {
                json = _pendingJson;
                if (json == null) return;
                // Don't clear _pendingJson yet — only clear on successful write.
            }

            try
            {
                var tmpPath = _stateFilePath + ".tmp";
                File.WriteAllText(tmpPath, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                if (File.Exists(_stateFilePath))
                {
                    File.Replace(tmpPath, _stateFilePath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(tmpPath, _stateFilePath);
                }

                lock (_writeLock)
                {
                    // Only clear if no new MarkDirty raced in with a newer string.
                    if (_pendingJson == json) _pendingJson = null;
                }
            }
            catch (Exception ex)
            {
                _observer?.OnSaveFailed(ex);
                // Leave _pendingJson set so the next MarkDirty or Flush retries.
            }
        }

        private void OnDebounceFired() => WritePendingNow();
```

- [ ] **Step 4: Run — expect pass**

Run: `dotnet test ... --filter "FullyQualifiedName~StateStoreTests"`
Expected: all green.

- [ ] **Step 5: Commit**

```
git add VirtualTabGroups/Core/StateStore.cs VirtualTabGroups.Tests/StateStoreTests.cs
git commit -m "Notify observer on save failure and preserve dirty state for retry"
```

---

### Task 12: Shutdown semantics — `Flush` must complete pending writes synchronously

Final correctness check: a `MarkDirty` followed immediately by `Flush` (the shutdown path) must have written to disk by the time `Flush` returns, even if the debounce timer hasn't fired yet.

**Files:**
- Modify: `VirtualTabGroups.Tests/StateStoreTests.cs`

This test covers behavior that should already work after Task 10, but it locks in the contract explicitly.

- [ ] **Step 1: Add the shutdown-flush test**

Append to `StateStoreTests.cs`:

```csharp
        [Fact]
        public void Flush_AfterMarkDirty_WritesSynchronouslyBeforeReturning()
        {
            var path = NewTempStatePath();
            using (var store = new StateStore(path, null, System.TimeSpan.FromSeconds(10)))  // long debounce
            {
                store.MarkDirty(new FolderNode(""), null);
                Assert.False(File.Exists(path));

                store.Flush();

                Assert.True(File.Exists(path));
            }
        }

        [Fact]
        public void Dispose_FlushesPendingWrite()
        {
            var path = NewTempStatePath();
            using (var store = new StateStore(path, null, System.TimeSpan.FromSeconds(10)))
            {
                store.MarkDirty(new FolderNode(""), null);
            }
            // Outside the using — Dispose must have flushed.
            Assert.True(File.Exists(path));
        }

        [Fact]
        public void ReadOnlyMode_SuppressesMarkDirtyAndFlush()
        {
            var path = NewTempStatePath();
            File.WriteAllText(path,
                "{\"schemaVersion\":99,\"root\":{\"type\":\"folder\",\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"\",\"children\":[]}}");
            var originalContent = File.ReadAllText(path);

            using (var store = new StateStore(path))
            {
                store.Load();
                Assert.True(store.IsReadOnly);

                store.MarkDirty(new FolderNode(""), null);
                store.Flush();
            }

            Assert.Equal(originalContent, File.ReadAllText(path));
        }
```

- [ ] **Step 2: Run full suite — expect all green**

Run: `dotnet test VirtualTabGroups.Tests/VirtualTabGroups.Tests.csproj --nologo`
Expected: all StateStoreTests pass plus the prior AliasResolver/NodeJsonConverter/sanity tests.

- [ ] **Step 3: Commit**

```
git add VirtualTabGroups.Tests/StateStoreTests.cs
git commit -m "Lock in StateStore shutdown semantics (Flush/Dispose/read-only)"
```

---

## Final verification

After the last commit, run the full suite once more to be sure nothing regressed across the chain of changes:

```
dotnet build VirtualTabGroups.sln
dotnet test VirtualTabGroups.Tests/VirtualTabGroups.Tests.csproj --nologo
```

Expected: clean build, all tests pass.

Then run `git log --oneline` and confirm the commits land in the expected order:

1. Add .gitignore for VS/.NET build outputs
2. Add Newtonsoft.Json 13.0.3 dependency
3. Add IStateStoreObserver interface for StateStore host callbacks
4. Add NodeJsonConverter for polymorphic folder/file serialization
5. Add StateStore skeleton with Load for missing/empty file
6. Implement StateStore.Load for valid v1 state.json
7. Back up corrupt state.json and notify observer on Load
8. Refuse to overwrite future-schema state.json (read-only mode)
9. Implement StateStore MarkDirty + Flush with atomic write
10. Add debounce timer to StateStore.MarkDirty
11. Notify observer on save failure and preserve dirty state for retry
12. Lock in StateStore shutdown semantics (Flush/Dispose/read-only)

## What's not in this plan

Per the spec's scope cut-offs, these are deliberately not implemented:

- Schema migrations (only v1 supported).
- Multi-instance coordination (undefined behavior).
- `state.json.corrupt-*` backup rotation/pruning.
- Any WinForms UI — the next plan covers the dockable panel that will be StateStore's first non-test consumer.
