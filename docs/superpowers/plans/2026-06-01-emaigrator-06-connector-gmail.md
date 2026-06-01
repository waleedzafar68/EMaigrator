# EMaigrator.Connectors.Gmail Implementation Plan

> Part of the EMaigrator v1 plan set — see 00-INDEX.md. Binds to CONTRACTS.md.

**Goal:** Implement the Gmail connector assembly `EMaigrator.Connectors.Gmail` — `GmailSourceProvider`, `GmailDestinationProvider`, and `GmailProviderPlugin` — over the Google Gmail v1 API, supporting BYO service-account + domain-wide delegation (DWD) impersonation, Gmail-label↔canonical-folder mapping, raw-message streaming reads, idempotent label-applied writes, and stable error normalization, all bound verbatim to the CONTRACTS.md provider abstractions.

**Architecture:** A single connector assembly that depends only on `EMaigrator.Core` abstractions (DESIGN.md §15 dependency rule). It uses `Google.Apis.Gmail.v1` with a `GoogleCredential` built transiently from a service-account JSON (held in `SecretBundle`, never written to disk), scoped minimally to `https://mail.google.com/` and impersonating the delegated mailbox via `CreateWithUser`. Gmail labels are the folder model: nested labels map to `FolderPath` segments via `/`, system labels (INBOX/SENT/etc.) map to canonical special-use folders, and SEEN/label state maps to `MessageFlags`+`Labels`. The provider is constructed by `GmailProviderPlugin` and is DI-discovered via `AddGmailConnector()`.

**Tech Stack:** C#/.NET 10, C# 13 (nullable enabled); `Google.Apis.Gmail.v1`, `Google.Apis.Auth`; xUnit + FluentAssertions + NSubstitute; `WireMock.Net` with recorded Gmail fixtures for HTTP-level contract tests. No live Google Workspace calls (paid-tenant live testing deferred per DESIGN.md §17 — documented risk).

---

### Task 1: Project scaffold, package references, and dependency-rule guard

**Goal:** Create the `EMaigrator.Connectors.Gmail` project (referencing only `EMaigrator.Core`) and its `EMaigrator.Connectors.Gmail.Tests` project with the Gmail SDK and WireMock packages wired, proving the assembly compiles and references nothing forbidden.

**Files:**
- Create: `src/EMaigrator.Connectors.Gmail/EMaigrator.Connectors.Gmail.csproj`
- Create: `src/EMaigrator.Connectors.Gmail/AssemblyMarker.cs`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/EMaigrator.Connectors.Gmail.Tests.csproj`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/ProjectReferenceGuardTests.cs`

**Acceptance Criteria:**
- [ ] `EMaigrator.Connectors.Gmail.csproj` targets `net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, references `EMaigrator.Core` via `ProjectReference` and `Google.Apis.Gmail.v1` + `Google.Apis.Auth` via `PackageReference`.
- [ ] The Gmail project has **no** `ProjectReference` to `EMaigrator.Infrastructure`, `EMaigrator.Workers`, `EMaigrator.Api`, or any other connector.
- [ ] The test project references the Gmail project, `xunit`, `FluentAssertions`, `NSubstitute`, and `WireMock.Net`.
- [ ] `dotnet build src/EMaigrator.Connectors.Gmail` succeeds.
- [ ] `ProjectReferenceGuardTests` passes (asserts referenced assembly names contain only `EMaigrator.Core`, `Google.*`, and framework assemblies).

**Verify:** `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~ProjectReferenceGuardTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Gmail.Tests/ProjectReferenceGuardTests.cs`:
   ```csharp
   using System.Linq;
   using EMaigrator.Connectors.Gmail;
   using FluentAssertions;
   using Xunit;

   namespace EMaigrator.Connectors.Gmail.Tests;

   public class ProjectReferenceGuardTests
   {
       [Fact]
       public void GmailAssembly_ReferencesOnlyCoreAndGoogleAndFramework()
       {
           var asm = typeof(AssemblyMarker).Assembly;
           var referenced = asm.GetReferencedAssemblies().Select(a => a.Name!).ToList();

           string[] forbidden =
           {
               "EMaigrator.Infrastructure",
               "EMaigrator.Workers",
               "EMaigrator.Api",
               "EMaigrator.Cli",
               "EMaigrator.Connectors.Imap",
               "EMaigrator.Connectors.Graph",
           };

           referenced.Should().NotContain(forbidden);
           referenced.Should().Contain("EMaigrator.Core");
       }
   }
   ```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~ProjectReferenceGuardTests` → expected FAIL: projects/types do not exist yet (`AssemblyMarker` and the csproj files are missing; compile error).
3. - [ ] Create `src/EMaigrator.Connectors.Gmail/EMaigrator.Connectors.Gmail.csproj`:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">

     <PropertyGroup>
       <TargetFramework>net10.0</TargetFramework>
       <LangVersion>13</LangVersion>
       <Nullable>enable</Nullable>
       <ImplicitUsings>enable</ImplicitUsings>
       <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
       <RootNamespace>EMaigrator.Connectors.Gmail</RootNamespace>
     </PropertyGroup>

     <ItemGroup>
       <ProjectReference Include="..\EMaigrator.Core\EMaigrator.Core.csproj" />
     </ItemGroup>

     <ItemGroup>
       <PackageReference Include="Google.Apis.Gmail.v1" Version="1.69.0.3680" />
       <PackageReference Include="Google.Apis.Auth" Version="1.69.0" />
     </ItemGroup>

   </Project>
   ```
   Create `src/EMaigrator.Connectors.Gmail/AssemblyMarker.cs`:
   ```csharp
   namespace EMaigrator.Connectors.Gmail;

   /// <summary>Stable type for assembly-level reflection in tests and DI scanning.</summary>
   public sealed class AssemblyMarker
   {
   }
   ```
   Create `src/EMaigrator.Connectors.Gmail.Tests/EMaigrator.Connectors.Gmail.Tests.csproj`:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">

     <PropertyGroup>
       <TargetFramework>net10.0</TargetFramework>
       <LangVersion>13</LangVersion>
       <Nullable>enable</Nullable>
       <ImplicitUsings>enable</ImplicitUsings>
       <IsPackable>false</IsPackable>
     </PropertyGroup>

     <ItemGroup>
       <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
       <PackageReference Include="xunit" Version="2.9.2" />
       <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
       <PackageReference Include="FluentAssertions" Version="6.12.2" />
       <PackageReference Include="NSubstitute" Version="5.3.0" />
       <PackageReference Include="WireMock.Net" Version="1.6.7" />
     </ItemGroup>

     <ItemGroup>
       <ProjectReference Include="..\EMaigrator.Connectors.Gmail\EMaigrator.Connectors.Gmail.csproj" />
     </ItemGroup>

     <ItemGroup>
       <None Update="Fixtures\**\*.json" CopyToOutputDirectory="PreserveNewest" />
     </ItemGroup>

   </Project>
   ```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~ProjectReferenceGuardTests` → expected PASS.
5. - [ ] Commit:
   ```
   git add src/EMaigrator.Connectors.Gmail src/EMaigrator.Connectors.Gmail.Tests
   git commit -m "feat(gmail): scaffold Gmail connector project with dependency-rule guard

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
   ```

---

### Task 2: Gmail ProviderConstraints

**Goal:** Define `GmailConstraints.Default` — the `ProviderConstraints` for Gmail's label model (no real depth limit, RFC822 35 MB message cap, 25 MB attachment cap, label-name illegal chars, reserved system-label names), exposed by both providers' `Constraints` property.

**Files:**
- Create: `src/EMaigrator.Connectors.Gmail/GmailConstraints.cs`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/GmailConstraintsTests.cs`

**Acceptance Criteria:**
- [ ] `GmailConstraints.Default` is a `ProviderConstraints` with `FolderSeparator == '/'`.
- [ ] `MaxMessageBytes == 35L * 1024 * 1024` (Gmail's ~35 MB total RFC822 import limit).
- [ ] `MaxAttachmentBytes == 25L * 1024 * 1024` (Gmail's 25 MB attachment limit).
- [ ] `MaxFolderDepth == int.MaxValue` (Gmail nested labels have no hard depth limit relevant here).
- [ ] `IllegalNameChars` contains `'/'` (the canonical separator is reserved inside a single label segment).
- [ ] `ReservedFolderNames` (case-insensitive set) contains `INBOX`, `SENT`, `DRAFT`, `SPAM`, `TRASH`, `STARRED`, `IMPORTANT`, `UNREAD`, `CHAT`, `CATEGORY_PERSONAL`.

**Verify:** `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailConstraintsTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Gmail.Tests/GmailConstraintsTests.cs`:
   ```csharp
   using EMaigrator.Connectors.Gmail;
   using FluentAssertions;
   using Xunit;

   namespace EMaigrator.Connectors.Gmail.Tests;

   public class GmailConstraintsTests
   {
       [Fact]
       public void Default_HasGmailSpecificLimits()
       {
           var c = GmailConstraints.Default;

           c.FolderSeparator.Should().Be('/');
           c.MaxMessageBytes.Should().Be(35L * 1024 * 1024);
           c.MaxAttachmentBytes.Should().Be(25L * 1024 * 1024);
           c.MaxFolderDepth.Should().Be(int.MaxValue);
           c.IllegalNameChars.Should().Contain('/');
       }

       [Fact]
       public void Default_ReservesSystemLabelNames()
       {
           var c = GmailConstraints.Default;

           c.ReservedFolderNames.Should().Contain(new[]
           {
               "INBOX", "SENT", "DRAFT", "SPAM", "TRASH",
               "STARRED", "IMPORTANT", "UNREAD", "CHAT", "CATEGORY_PERSONAL",
           });
       }
   }
   ```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailConstraintsTests` → expected FAIL: `GmailConstraints` does not exist (compile error).
3. - [ ] Implement `src/EMaigrator.Connectors.Gmail/GmailConstraints.cs`:
   ```csharp
   using EMaigrator.Core.Abstractions;

   namespace EMaigrator.Connectors.Gmail;

   /// <summary>
   /// Provider constraints for Gmail. Gmail uses a flat-label model exposed to the
   /// canonical engine as nested folders via the '/' separator; system labels are reserved.
   /// </summary>
   public static class GmailConstraints
   {
       public static readonly ProviderConstraints Default = new()
       {
           MaxFolderDepth = int.MaxValue,
           MaxPathLengthChars = 225, // Gmail rejects label names longer than 225 chars
           IllegalNameChars = new[] { '/' },
           MaxMessageBytes = 35L * 1024 * 1024,    // ~35 MB total RFC822 size on import/insert
           MaxAttachmentBytes = 25L * 1024 * 1024, // 25 MB per-attachment limit
           FolderSeparator = '/',
           ReservedFolderNames = new[]
           {
               "INBOX", "SENT", "DRAFT", "DRAFTS", "SPAM", "TRASH",
               "STARRED", "IMPORTANT", "UNREAD", "CHAT",
               "CATEGORY_PERSONAL", "CATEGORY_SOCIAL", "CATEGORY_PROMOTIONS",
               "CATEGORY_UPDATES", "CATEGORY_FORUMS",
           },
       };
   }
   ```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailConstraintsTests` → expected PASS.
5. - [ ] Commit:
   ```
   git add src/EMaigrator.Connectors.Gmail/GmailConstraints.cs src/EMaigrator.Connectors.Gmail.Tests/GmailConstraintsTests.cs
   git commit -m "feat(gmail): declare Gmail provider constraints (label model, size caps)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
   ```

---

### Task 3: Label↔FolderPath mapping (nested labels, system-label special cases)

**Goal:** Implement the pure `GmailLabelMapper` translating Gmail label names ↔ canonical `FolderPath`, including nested labels via `/`, system-label→special-use mapping (INBOX→root inbox, SENT, DRAFT, SPAM, TRASH), and the All-Mail / Sent edge cases.

**Files:**
- Create: `src/EMaigrator.Connectors.Gmail/GmailLabelMapper.cs`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/GmailLabelMapperTests.cs`

**Acceptance Criteria:**
- [ ] `LabelNameToFolderPath("Work/Clients/Acme")` → `FolderPath` with segments `["Work","Clients","Acme"]`.
- [ ] `LabelNameToFolderPath("INBOX")` → `FolderPath` segments `["INBOX"]`.
- [ ] `FolderPathToLabelName(FolderPath.Parse("Work/Clients/Acme"))` → `"Work/Clients/Acme"`.
- [ ] `IsSystemLabel("INBOX")`, `IsSystemLabel("SENT")`, `IsSystemLabel("CATEGORY_PROMOTIONS")` → true; `IsSystemLabel("Work")` → false.
- [ ] `IsAllMail` returns true only for the synthetic `"[Gmail]/All Mail"` path and false for any other path (e.g. `INBOX`, `Work/All Mail`).
- [ ] `IsMappableLabel` (name-based) returns false for the non-folder state labels `CHAT` and `UNREAD`, and true for `INBOX`, `SENT`, `Work`.
- [ ] Round-trip: for any user label name without illegal chars, `FolderPathToLabelName(LabelNameToFolderPath(n)) == n`.

**Verify:** `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailLabelMapperTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Gmail.Tests/GmailLabelMapperTests.cs`:
   ```csharp
   using EMaigrator.Connectors.Gmail;
   using EMaigrator.Core.Model;
   using FluentAssertions;
   using Xunit;

   namespace EMaigrator.Connectors.Gmail.Tests;

   public class GmailLabelMapperTests
   {
       [Theory]
       [InlineData("Work/Clients/Acme", new[] { "Work", "Clients", "Acme" })]
       [InlineData("INBOX", new[] { "INBOX" })]
       [InlineData("SENT", new[] { "SENT" })]
       public void LabelNameToFolderPath_SplitsNestedAndSystemLabels(string label, string[] expected)
       {
           var fp = GmailLabelMapper.LabelNameToFolderPath(label);
           fp.Segments.Should().Equal(expected);
       }

       [Theory]
       [InlineData(new[] { "Work", "Clients", "Acme" }, "Work/Clients/Acme")]
       [InlineData(new[] { "INBOX" }, "INBOX")]
       public void FolderPathToLabelName_JoinsWithSlash(string[] segments, string expected)
       {
           var fp = new FolderPath(segments);
           GmailLabelMapper.FolderPathToLabelName(fp).Should().Be(expected);
       }

       [Theory]
       [InlineData("INBOX", true)]
       [InlineData("SENT", true)]
       [InlineData("CATEGORY_PROMOTIONS", true)]
       [InlineData("Work", false)]
       [InlineData("Work/Clients", false)]
       public void IsSystemLabel_DetectsReservedNames(string label, bool expected)
           => GmailLabelMapper.IsSystemLabel(label).Should().Be(expected);

       [Theory]
       [InlineData("CHAT", false)]
       [InlineData("UNREAD", false)]
       [InlineData("INBOX", true)]
       [InlineData("SENT", true)]
       [InlineData("Work", true)]
       public void IsMappableLabel_ExcludesChatAndUnread(string label, bool expected)
           => GmailLabelMapper.IsMappableLabel(label).Should().Be(expected);

       [Fact]
       public void IsAllMail_OnlyTrueForSyntheticAllMailPath()
       {
           GmailLabelMapper.IsAllMail(FolderPath.Parse("[Gmail]/All Mail")).Should().BeTrue();
           GmailLabelMapper.IsAllMail(FolderPath.Parse("INBOX")).Should().BeFalse();
           GmailLabelMapper.IsAllMail(FolderPath.Parse("Work/All Mail")).Should().BeFalse();
       }

       [Theory]
       [InlineData("Work")]
       [InlineData("Work/Clients")]
       [InlineData("Receipts 2026")]
       public void RoundTrip_PreservesUserLabelNames(string name)
       {
           var fp = GmailLabelMapper.LabelNameToFolderPath(name);
           GmailLabelMapper.FolderPathToLabelName(fp).Should().Be(name);
       }
   }
   ```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailLabelMapperTests` → expected FAIL: `GmailLabelMapper` does not exist (compile error).
3. - [ ] Implement `src/EMaigrator.Connectors.Gmail/GmailLabelMapper.cs`:
   ```csharp
   using System.Collections.Generic;
   using System.Linq;
   using EMaigrator.Core.Model;

   namespace EMaigrator.Connectors.Gmail;

   /// <summary>
   /// Pure translation between Gmail label names and canonical <see cref="FolderPath"/>.
   /// Gmail nests labels with '/', matching the canonical separator. System labels
   /// (INBOX, SENT, etc.) are reserved; CHAT is not migratable as a folder; the synthetic
   /// "[Gmail]/All Mail" path is treated specially so reads never double-copy.
   /// </summary>
   public static class GmailLabelMapper
   {
       public const string AllMailPath = "[Gmail]/All Mail";

       private static readonly HashSet<string> SystemLabels = new(System.StringComparer.OrdinalIgnoreCase)
       {
           "INBOX", "SENT", "DRAFT", "SPAM", "TRASH", "STARRED", "IMPORTANT", "UNREAD", "CHAT",
           "CATEGORY_PERSONAL", "CATEGORY_SOCIAL", "CATEGORY_PROMOTIONS", "CATEGORY_UPDATES", "CATEGORY_FORUMS",
       };

       /// <summary>
       /// State/virtual labels that are never migratable as ordinary folders: CHAT (Hangouts/Chat
       /// history, not mail) and UNREAD (a read-state flag, surfaced via <see cref="MessageFlags"/>,
       /// never a folder).
       /// </summary>
       private static readonly HashSet<string> NonMappable = new(System.StringComparer.OrdinalIgnoreCase)
       {
           "CHAT", "UNREAD",
       };

       public static bool IsSystemLabel(string labelName) => SystemLabels.Contains(labelName);

       public static bool IsMappableLabel(string labelName) => !NonMappable.Contains(labelName);

       public static bool IsAllMail(FolderPath path)
           => path.ToString() == AllMailPath;

       public static FolderPath LabelNameToFolderPath(string labelName)
       {
           var segments = labelName.Split('/').Where(s => s.Length > 0).ToList();
           return new FolderPath(segments);
       }

       public static string FolderPathToLabelName(FolderPath path)
           => string.Join('/', path.Segments);
   }
   ```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailLabelMapperTests` → expected PASS.
5. - [ ] Commit:
   ```
   git add src/EMaigrator.Connectors.Gmail/GmailLabelMapper.cs src/EMaigrator.Connectors.Gmail.Tests/GmailLabelMapperTests.cs
   git commit -m "feat(gmail): label<->FolderPath mapper with system-label and All-Mail handling

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
   ```

---

### Task 4: SEEN/label state → MessageFlags + Labels mapping

**Goal:** Implement the pure `GmailFlagMapper` that converts a Gmail message's `LabelIds` into canonical `MessageFlags` (UNREAD absent ⇒ `Seen`; STARRED ⇒ `Flagged`; DRAFT ⇒ `Draft`) and into the canonical `Labels` list (user labels only, system labels stripped except where they carry meaning).

**Files:**
- Create: `src/EMaigrator.Connectors.Gmail/GmailFlagMapper.cs`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/GmailFlagMapperTests.cs`

**Acceptance Criteria:**
- [ ] A message with label IDs not containing `UNREAD` ⇒ `MessageFlags.Seen` set.
- [ ] A message **containing** `UNREAD` ⇒ `Seen` **not** set.
- [ ] `STARRED` ⇒ `MessageFlags.Flagged`; `DRAFT` ⇒ `MessageFlags.Draft`.
- [ ] `ToCanonicalLabels` returns only user-label *names* (resolved via an id→name dictionary), excluding system labels like `INBOX`, `UNREAD`, `CATEGORY_*`.
- [ ] Combined flags compose (e.g. STARRED + UNREAD ⇒ `Flagged` and not `Seen`).

**Verify:** `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailFlagMapperTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Gmail.Tests/GmailFlagMapperTests.cs`:
   ```csharp
   using System.Collections.Generic;
   using EMaigrator.Connectors.Gmail;
   using EMaigrator.Core.Model;
   using FluentAssertions;
   using Xunit;

   namespace EMaigrator.Connectors.Gmail.Tests;

   public class GmailFlagMapperTests
   {
       [Fact]
       public void NoUnreadLabel_MeansSeen()
       {
           var flags = GmailFlagMapper.ToFlags(new[] { "INBOX" });
           flags.Should().HaveFlag(MessageFlags.Seen);
       }

       [Fact]
       public void UnreadLabel_MeansNotSeen()
       {
           var flags = GmailFlagMapper.ToFlags(new[] { "INBOX", "UNREAD" });
           flags.Should().NotHaveFlag(MessageFlags.Seen);
       }

       [Fact]
       public void StarredAndDraft_MapToFlaggedAndDraft()
       {
           var flags = GmailFlagMapper.ToFlags(new[] { "STARRED", "DRAFT", "UNREAD" });
           flags.Should().HaveFlag(MessageFlags.Flagged);
           flags.Should().HaveFlag(MessageFlags.Draft);
           flags.Should().NotHaveFlag(MessageFlags.Seen);
       }

       [Fact]
       public void ToCanonicalLabels_ReturnsOnlyUserLabelNames()
       {
           var idToName = new Dictionary<string, string>
           {
               ["INBOX"] = "INBOX",
               ["UNREAD"] = "UNREAD",
               ["CATEGORY_PROMOTIONS"] = "CATEGORY_PROMOTIONS",
               ["Label_42"] = "Work/Clients/Acme",
           };

           var labels = GmailFlagMapper.ToCanonicalLabels(
               new[] { "INBOX", "UNREAD", "CATEGORY_PROMOTIONS", "Label_42" }, idToName);

           labels.Should().Equal(new[] { "Work/Clients/Acme" });
       }
   }
   ```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailFlagMapperTests` → expected FAIL: `GmailFlagMapper` does not exist (compile error).
3. - [ ] Implement `src/EMaigrator.Connectors.Gmail/GmailFlagMapper.cs`:
   ```csharp
   using System.Collections.Generic;
   using System.Linq;
   using EMaigrator.Core.Model;

   namespace EMaigrator.Connectors.Gmail;

   /// <summary>
   /// Maps a Gmail message's label-id set into canonical <see cref="MessageFlags"/> and the
   /// canonical user-label list. Gmail models read-state as the *absence* of the UNREAD label.
   /// </summary>
   public static class GmailFlagMapper
   {
       public static MessageFlags ToFlags(IReadOnlyCollection<string> labelIds)
       {
           var set = new HashSet<string>(labelIds, System.StringComparer.OrdinalIgnoreCase);
           var flags = MessageFlags.None;

           // Read-state: UNREAD present => not seen; absent => seen.
           if (!set.Contains("UNREAD"))
               flags |= MessageFlags.Seen;

           if (set.Contains("STARRED"))
               flags |= MessageFlags.Flagged;

           if (set.Contains("DRAFT"))
               flags |= MessageFlags.Draft;

           return flags;
       }

       /// <summary>
       /// Returns the human-readable names of user labels only (system labels excluded),
       /// resolving id->name via the provided label map.
       /// </summary>
       public static IReadOnlyList<string> ToCanonicalLabels(
           IReadOnlyCollection<string> labelIds,
           IReadOnlyDictionary<string, string> labelIdToName)
       {
           return labelIds
               .Where(id => !GmailLabelMapper.IsSystemLabel(LookupName(id, labelIdToName)))
               .Select(id => LookupName(id, labelIdToName))
               .Where(name => name.Length > 0)
               .ToList();
       }

       private static string LookupName(string id, IReadOnlyDictionary<string, string> map)
           => map.TryGetValue(id, out var name) ? name : id;
   }
   ```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailFlagMapperTests` → expected PASS.
5. - [ ] Commit:
   ```
   git add src/EMaigrator.Connectors.Gmail/GmailFlagMapper.cs src/EMaigrator.Connectors.Gmail.Tests/GmailFlagMapperTests.cs
   git commit -m "feat(gmail): map Gmail label state to canonical MessageFlags and labels

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
   ```

---

### Task 5: Error normalization (Gmail HTTP/quota → stable errorSignature)

**Goal:** Implement `GmailErrorNormalizer.Normalize(Exception)` producing a stable `gmail:<httpStatus>:<reason>` signature (per CONTRACTS §8) for catalog matching — mapping 429/`rateLimitExceeded`/`userRateLimitExceeded`/`quotaExceeded`, 403, 401, 404, 5xx — and a `RetryAfter` extractor, **without** leaking the impersonated mailbox or SA identity into the signature.

**Files:**
- Create: `src/EMaigrator.Connectors.Gmail/GmailErrorNormalizer.cs`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/GmailErrorNormalizerTests.cs`

**Acceptance Criteria:**
- [ ] A `GoogleApiException` with `HttpStatusCode == 429` and error reason `rateLimitExceeded` normalizes to `"gmail:429:rateLimitExceeded"`.
- [ ] `quotaExceeded` (403 or 429) normalizes to `"gmail:<status>:quotaExceeded"`.
- [ ] A 401 invalid-credentials error normalizes to `"gmail:401:authError"`.
- [ ] A 404 normalizes to `"gmail:404:notFound"`.
- [ ] Unknown/non-Google exceptions normalize to `"gmail:unknown"`.
- [ ] The produced signature contains **no** email address and **no** `@`-containing substring even if the original exception message embeds the impersonated user (test asserts absence).
- [ ] `TryParseRetryAfter` parses a raw HTTP `Retry-After` header value: returns the `TimeSpan` for a numeric delta-seconds value (e.g. `"30"` → 30s), returns `null` for a null/empty/non-numeric value, and clamps negative values to `null`.

**Verify:** `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailErrorNormalizerTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Gmail.Tests/GmailErrorNormalizerTests.cs`:
   ```csharp
   using System;
   using System.Net;
   using EMaigrator.Connectors.Gmail;
   using FluentAssertions;
   using Google;
   using Google.Apis.Requests;
   using Xunit;

   namespace EMaigrator.Connectors.Gmail.Tests;

   public class GmailErrorNormalizerTests
   {
       private static GoogleApiException MakeApiException(HttpStatusCode status, string reason, string message)
       {
           var err = new RequestError
           {
               Code = (int)status,
               Message = message,
               Errors = new System.Collections.Generic.List<SingleError>
               {
                   new SingleError { Reason = reason, Message = message },
               },
           };
           return new GoogleApiException("gmail", message) { HttpStatusCode = status, Error = err };
       }

       [Theory]
       [InlineData(HttpStatusCode.TooManyRequests, "rateLimitExceeded", "gmail:429:rateLimitExceeded")]
       [InlineData(HttpStatusCode.TooManyRequests, "userRateLimitExceeded", "gmail:429:userRateLimitExceeded")]
       [InlineData(HttpStatusCode.Forbidden, "quotaExceeded", "gmail:403:quotaExceeded")]
       [InlineData(HttpStatusCode.Unauthorized, "authError", "gmail:401:authError")]
       [InlineData(HttpStatusCode.NotFound, "notFound", "gmail:404:notFound")]
       public void Normalize_MapsKnownGoogleErrors(HttpStatusCode status, string reason, string expected)
       {
           var ex = MakeApiException(status, reason, "boom");
           GmailErrorNormalizer.Normalize(ex).Should().Be(expected);
       }

       [Fact]
       public void Normalize_UnknownException_ReturnsGenericSignature()
       {
           GmailErrorNormalizer.Normalize(new InvalidOperationException("nope"))
               .Should().Be("gmail:unknown");
       }

       [Fact]
       public void Normalize_DoesNotLeakImpersonatedMailbox()
       {
           var ex = MakeApiException(
               HttpStatusCode.Forbidden, "quotaExceeded",
               "User rate limit exceeded for victim@example.com (project 12345)");
           var sig = GmailErrorNormalizer.Normalize(ex);

           sig.Should().Be("gmail:403:quotaExceeded");
           sig.Should().NotContain("@");
           sig.Should().NotContain("victim");
       }

       [Theory]
       [InlineData("30", 30)]
       [InlineData("0", 0)]
       [InlineData("120", 120)]
       public void TryParseRetryAfter_ReturnsSeconds_ForNumericValue(string header, int expectedSeconds)
       {
           GmailErrorNormalizer.TryParseRetryAfter(header)
               .Should().Be(TimeSpan.FromSeconds(expectedSeconds));
       }

       [Theory]
       [InlineData(null)]
       [InlineData("")]
       [InlineData("   ")]
       [InlineData("not-a-number")]
       [InlineData("-5")]
       public void TryParseRetryAfter_ReturnsNull_ForMissingOrInvalidValue(string? header)
       {
           GmailErrorNormalizer.TryParseRetryAfter(header).Should().BeNull();
       }
   }
   ```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailErrorNormalizerTests` → expected FAIL: `GmailErrorNormalizer` does not exist (compile error).
3. - [ ] Implement `src/EMaigrator.Connectors.Gmail/GmailErrorNormalizer.cs`:
   ```csharp
   using System;
   using System.Linq;
   using Google;

   namespace EMaigrator.Connectors.Gmail;

   /// <summary>
   /// Normalizes Gmail/Google API failures into a stable, credential-free error signature
   /// of the form "gmail:&lt;status&gt;:&lt;reason&gt;" for catalog matching (CONTRACTS §8).
   /// The signature deliberately omits the impersonated mailbox and SA identity so quota
   /// errors never leak account identity to end users (DESIGN.md §10).
   /// </summary>
   public static class GmailErrorNormalizer
   {
       public static string Normalize(Exception ex)
       {
           if (ex is not GoogleApiException gex)
               return "gmail:unknown";

           var status = gex.HttpStatusCode == 0 ? "unknown" : ((int)gex.HttpStatusCode).ToString();
           var reason = gex.Error?.Errors?.FirstOrDefault()?.Reason;

           if (string.IsNullOrWhiteSpace(reason))
               reason = status switch
               {
                   "401" => "authError",
                   "403" => "forbidden",
                   "404" => "notFound",
                   "429" => "rateLimitExceeded",
                   _ => "error",
               };

           // Reason values come from a closed Google vocabulary (rateLimitExceeded, quotaExceeded,
           // userRateLimitExceeded, authError, notFound, ...) — they never contain PII. We still
           // strip anything past whitespace defensively so no free-text/email can ride along.
           reason = new string(reason.TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());

           return $"gmail:{status}:{reason}";
       }

       /// <summary>
       /// Parses a raw HTTP <c>Retry-After</c> header value (delta-seconds form) into a
       /// <see cref="TimeSpan"/>. Google's typed exception does not expose response headers, so the
       /// provider passes the header it read off the HTTP response. Returns null for a null/empty/
       /// non-numeric value, and clamps negative deltas to null. The HTTP-date form is not used by
       /// Gmail quota responses and is intentionally not parsed here.
       /// </summary>
       public static TimeSpan? TryParseRetryAfter(string? retryAfterHeader)
       {
           if (string.IsNullOrWhiteSpace(retryAfterHeader))
               return null;

           if (!int.TryParse(retryAfterHeader.Trim(), out var seconds) || seconds < 0)
               return null;

           return TimeSpan.FromSeconds(seconds);
       }
   }
   ```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailErrorNormalizerTests` → expected PASS.
5. - [ ] Commit:
   ```
   git add src/EMaigrator.Connectors.Gmail/GmailErrorNormalizer.cs src/EMaigrator.Connectors.Gmail.Tests/GmailErrorNormalizerTests.cs
   git commit -m "feat(gmail): normalize Gmail errors to stable credential-free signatures

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
   ```

---

### Task 6: GmailServiceFactory — transient DWD credential + GmailService construction

**Goal:** Implement `GmailServiceFactory` that builds a `GmailService` from a `ConnectionDescriptor` + `SecretBundle`, parsing the SA JSON from the bundle, scoping **only** to `https://mail.google.com/`, impersonating the delegated user (`Settings["accountEmail"]`), holding the JSON only transiently (never to disk), and exposing the scope/user used so tests can assert least-privilege.

**Files:**
- Create: `src/EMaigrator.Connectors.Gmail/GmailServiceFactory.cs`
- Create: `src/EMaigrator.Connectors.Gmail/GmailConnectionConfig.cs`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/GmailServiceFactoryTests.cs`

**Acceptance Criteria:**
- [ ] `GmailConnectionConfig.FromDescriptor(descriptor, secrets)` reads `Settings["accountEmail"]` (the delegated/impersonated mailbox) and `secrets.Values["serviceAccountJson"]`, throwing `ArgumentException` with a message that contains **no** secret value if either is missing.
- [ ] `GmailServiceFactory.RequiredScopes` is exactly `["https://mail.google.com/"]` (single, minimal scope) — asserted by test.
- [ ] `GmailServiceFactory.Create(config)` returns a `GmailService` whose `HttpClientInitializer` is a `GoogleCredential`; the factory does **not** write the SA JSON to any file (test asserts no temp file is created and the JSON string is not retained on the factory).
- [ ] The SA JSON is parsed via `GoogleCredential.FromJson` (in-memory) — verified by a test feeding a syntactically valid minimal SA JSON and asserting no exception from parsing/scoping.
- [ ] `GmailConnectionConfig` does **not** expose the raw JSON via any public property (test asserts via reflection that no public string property returns the JSON).

**Verify:** `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailServiceFactoryTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Gmail.Tests/GmailServiceFactoryTests.cs`:
   ```csharp
   using System;
   using System.Collections.Generic;
   using System.IO;
   using System.Linq;
   using EMaigrator.Connectors.Gmail;
   using EMaigrator.Core.Abstractions;
   using EMaigrator.Core.Model;
   using FluentAssertions;
   using Xunit;

   namespace EMaigrator.Connectors.Gmail.Tests;

   public class GmailServiceFactoryTests
   {
       // A syntactically valid (fake) service-account JSON with a real RSA test key so
       // GoogleCredential.FromJson succeeds. The key is a throwaway generated for tests only.
       private static string FakeServiceAccountJson() => TestServiceAccount.Json;

       private static ConnectionDescriptor Descriptor(string? email = "target@example.com") => new()
       {
           Provider = new ProviderId("gmail"),
           Auth = AuthMethod.GmailServiceAccountDwd,
           Settings = new Dictionary<string, string>
           {
               ["accountEmail"] = email ?? "",
           },
       };

       [Fact]
       public void RequiredScopes_IsSingleMailGoogleComScope()
       {
           GmailServiceFactory.RequiredScopes.Should().Equal(new[] { "https://mail.google.com/" });
       }

       [Fact]
       public void FromDescriptor_MissingEmail_ThrowsWithoutLeakingSecret()
       {
           var secrets = new SecretBundle(new Dictionary<string, string> { ["serviceAccountJson"] = FakeServiceAccountJson() });
           var act = () => GmailConnectionConfig.FromDescriptor(Descriptor(email: ""), secrets);
           act.Should().Throw<ArgumentException>()
              .Which.Message.Should().NotContain("PRIVATE KEY");
       }

       [Fact]
       public void FromDescriptor_MissingJson_ThrowsWithoutLeakingSecret()
       {
           var secrets = new SecretBundle(new Dictionary<string, string>());
           var act = () => GmailConnectionConfig.FromDescriptor(Descriptor(), secrets);
           act.Should().Throw<ArgumentException>()
              .Which.Message.Should().NotContain("BEGIN");
       }

       [Fact]
       public void Create_BuildsServiceWithoutWritingJsonToDisk()
       {
           var secrets = new SecretBundle(new Dictionary<string, string> { ["serviceAccountJson"] = FakeServiceAccountJson() });
           var config = GmailConnectionConfig.FromDescriptor(Descriptor(), secrets);

           var tempBefore = Directory.GetFiles(Path.GetTempPath()).Length;
           using var service = GmailServiceFactory.Create(config);
           var tempAfter = Directory.GetFiles(Path.GetTempPath()).Length;

           service.Should().NotBeNull();
           service.HttpClientInitializer.Should().NotBeNull();
           tempAfter.Should().Be(tempBefore, "the SA JSON must be parsed in-memory, never spilled to a temp file");
       }

       [Fact]
       public void Config_DoesNotExposeRawJsonViaPublicProperty()
       {
           var json = FakeServiceAccountJson();
           var secrets = new SecretBundle(new Dictionary<string, string> { ["serviceAccountJson"] = json });
           var config = GmailConnectionConfig.FromDescriptor(Descriptor(), secrets);

           var leaking = config.GetType()
               .GetProperties()
               .Where(p => p.PropertyType == typeof(string))
               .Select(p => (string?)p.GetValue(config))
               .Any(v => v != null && v.Contains("PRIVATE KEY"));

           leaking.Should().BeFalse();
       }
   }
   ```
2. - [ ] Create the test key helper `src/EMaigrator.Connectors.Gmail.Tests/TestServiceAccount.cs` exactly as below. It embeds a complete, throwaway 2048-bit RSA PKCS#8 key (a non-secret test fixture) so `GoogleCredential.FromJson` succeeds fully offline — paste it verbatim, no edits required:
   ```csharp
   namespace EMaigrator.Connectors.Gmail.Tests;

   /// <summary>
   /// Fake service-account JSON for offline tests. The private key is a throwaway RSA key
   /// generated solely for unit testing (no access to any real Google project). Regenerate with:
   ///   openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out test.pem
   /// then paste PEM with literal \n line breaks below.
   /// </summary>
   public static class TestServiceAccount
   {
       // Throwaway 2048-bit RSA PKCS#8 key generated solely for offline unit tests (no real Google
       // project). It is a non-secret test fixture; it never authenticates against any live tenant.
       private const string PrivateKeyPem =
           "-----BEGIN PRIVATE KEY-----\\nMIIEugIBADANBgkqhkiG9w0BAQEFAASCBKQwggSgAgEAAoIBAQCPNV37uQ7cHZQc\\nYBE0nXdh+f/Cw2g7Q4NUZIh1sOXCWIHk3ScKYZRYmu4gATw5P2J2QLRSaFuzrLC+\\nlyLNLmWjwFhivZ5sjoHOAw+88BoyA7+kCMSSIYvSijshJvdNUvBO99YnTKR8Hogc\\nIoG4xvv0BVdXW37+KRTss+KNERXgWstVHchD7LEPkmSdn4Uvk5vQPPbtPk+RXGk0\\n23zp5E8KTfEjeg4UNDANJsONsYyRg2/N37b7bN9IRxLC4xi3xjYwn1ibouj1DAos\\nGOweLPY7kB6sTkX6m6Q7QILpFVb0Rv7HsGMtR2p31ITpGEjH2LlOq8RJxdfeYSfE\\nIArt3I7RAgMBAAECgf92Xwuq4JwtH9snroCKQl5GLflUvhA5wY7xrZIzNbUJn1Q+\\nyE4CLAYNTIKSZx2lyYP+xXJHa4Xg+K0J3K3My2etTXUWqNrPquYVqDzUqeH9Kqa3\\n/5ymQp7rDYyH1Ug03IlQZ1WBxmgZ8A1cCWm67JRLGjRyO2liIV2aXwC0JEXg20RR\\nShtFHlU+CiIkymZiUAbQ7QV2BNtvT8mA5cbpApTD2FzcDiUdijgUSLZW8Rj+NHeV\\nIR5xsspIwPUOUQJNxhYMHC7T7NjtavpEyqo5bjl4Z/4V7pJykRaJ6gheNYJ8wiIq\\ngfo5vbnMhqh3e/WWZPW68ZCSuQSkSBeC5FKTp4ECgYEAw5i9ATQNPlhIgVkpTPk+\\n1vP2WwwPN3faEb3qV0vVdNXpW9r9OcKd/QhZwidknzwNHcBFQPdfUMx5/VSqHfKD\\ndq6OZDDgLgUq4YDcoUEZHcjJ9fNEXjQiJPQ0fSyD4TBzqObiVJNwGQmCAZUf9m3V\\ntiYR/YNdU6wtwdhSA9gUhoECgYEAu277rJnlhhcK7TP4JTLSmJji+Krez8TR8hCD\\n3+h3de/TWkjDZVbh/LxcJA6hSUHXDW1Jw+I/h8zhbxXtsQDavLM0PU6tpiurvWaH\\nzyQ9XlPQ91t/A+xQkqIBrYh0rGWqaKVg+cb4N9m9qHZ0KgnwM/rSB0cbQXFT6AYw\\nLCxeAFECgYAoHKqmFIaiwngcDqzpnDPG4UEkatS0C2AtQ0VLocGktDmnHMHRlpfP\\nzGab6ng4L5iBAW0yZYimiUh7K2G3woQzUpjg8yUGSwkANe0JJNCByyufxMPAjfBy\\no6IgCYECLW2Ktc60iYfzmn+O04Y6g0vQjv4hf08kWasIldQ79ZRAAQKBgFIW7nUO\\nxf6vUuLGkxS/qIqa0zVzqLg4jHbHEura5o8ppVhya9mTbtCBMp28JpluE6DWz6rS\\nCV8RtV4wrXSLWkGw/t0m+1i+4a3HHQ304kfQz8G2Oe/e7P77o158WBU1RaglXk6m\\n/QmA/NauYnwS9Dffz2LOmrpTxxrksu511AmxAoGAFKf9MNH1uEPECl9R2lwMPS+Q\\nIw7CQQF+DxbgHEf646J93oO+ptLYrYIcidiUDgTbg35dC6u+Kl3Rw4r5V6EmAptT\\nAagmnjhTpZSJGuY622KWcCAhb481TL1mpz+loH84jT8838R25EsrOvj5oHQGHgta\\nF1gOBMUDWvldK/WoaG0=\\n-----END PRIVATE KEY-----\\n";

       public static string Json => "{" +
           "\"type\":\"service_account\"," +
           "\"project_id\":\"test-project\"," +
           "\"private_key_id\":\"00000000000000000000000000000000deadbeef\"," +
           "\"private_key\":\"" + PrivateKeyPem + "\"," +
           "\"client_email\":\"emaigrator-test@test-project.iam.gserviceaccount.com\"," +
           "\"client_id\":\"123456789012345678901\"," +
           "\"auth_uri\":\"https://accounts.google.com/o/oauth2/auth\"," +
           "\"token_uri\":\"https://oauth2.googleapis.com/token\"," +
           "\"auth_provider_x509_cert_url\":\"https://www.googleapis.com/oauth2/v1/certs\"," +
           "\"client_x509_cert_url\":\"https://www.googleapis.com/robot/v1/metadata/x509/emaigrator-test%40test-project.iam.gserviceaccount.com\"" +
           "}";
   }
   ```
   The embedded `PrivateKeyPem` above is already a complete, valid throwaway key — no generation step is required. (Only if you ever want to rotate it: run `openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out test.pem`, then re-join the PEM lines with literal `\n` and paste over `PrivateKeyPem`.)
3. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailServiceFactoryTests` → expected FAIL: `GmailConnectionConfig` / `GmailServiceFactory` do not exist (compile error).
4. - [ ] Implement `src/EMaigrator.Connectors.Gmail/GmailConnectionConfig.cs`:
   ```csharp
   using System;
   using EMaigrator.Core.Abstractions;

   namespace EMaigrator.Connectors.Gmail;

   /// <summary>
   /// Validated, in-memory Gmail connection config. The service-account JSON is held only
   /// transiently for credential construction and is never exposed via a public property,
   /// never logged, and never written to disk (DESIGN.md §10).
   /// </summary>
   public sealed class GmailConnectionConfig
   {
       private readonly string _serviceAccountJson;

       private GmailConnectionConfig(string delegatedUser, string serviceAccountJson)
       {
           DelegatedUser = delegatedUser;
           _serviceAccountJson = serviceAccountJson;
       }

       /// <summary>The mailbox being impersonated via domain-wide delegation.</summary>
       public string DelegatedUser { get; }

       /// <summary>Internal accessor for the factory only; not a public-data surface.</summary>
       internal string ServiceAccountJson => _serviceAccountJson;

       public static GmailConnectionConfig FromDescriptor(ConnectionDescriptor descriptor, SecretBundle secrets)
       {
           if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));
           if (secrets is null) throw new ArgumentNullException(nameof(secrets));

           if (!descriptor.Settings.TryGetValue("accountEmail", out var email) || string.IsNullOrWhiteSpace(email))
               throw new ArgumentException("Gmail connection requires a non-empty 'accountEmail' (the delegated mailbox).", nameof(descriptor));

           if (!secrets.Values.TryGetValue("serviceAccountJson", out var json) || string.IsNullOrWhiteSpace(json))
               throw new ArgumentException("Gmail connection requires a 'serviceAccountJson' secret.", nameof(secrets));

           return new GmailConnectionConfig(email.Trim(), json);
       }
   }
   ```
   Implement `src/EMaigrator.Connectors.Gmail/GmailServiceFactory.cs`:
   ```csharp
   using System.Collections.Generic;
   using Google.Apis.Auth.OAuth2;
   using Google.Apis.Gmail.v1;
   using Google.Apis.Services;

   namespace EMaigrator.Connectors.Gmail;

   /// <summary>
   /// Builds an authenticated <see cref="GmailService"/> using a BYO service account with
   /// domain-wide delegation. Scope is intentionally the single broad-but-necessary
   /// "https://mail.google.com/" (the only scope that authorizes raw RFC822 read AND
   /// messages.import/insert with internalDate). The SA JSON is parsed in-memory only.
   /// </summary>
   public static class GmailServiceFactory
   {
       /// <summary>
       /// Least-privilege scope set. https://mail.google.com/ is required because Gmail's
       /// readonly scope cannot fetch format=raw with full fidelity, and import/insert require
       /// full mail access; no narrower scope authorizes both directions. Justification recorded
       /// in the Security Verification task.
       /// </summary>
       public static readonly IReadOnlyList<string> RequiredScopes = new[] { "https://mail.google.com/" };

       public const string ApplicationName = "EMaigrator";

       public static GmailService Create(GmailConnectionConfig config)
       {
           var credential = GoogleCredential
               .FromJson(config.ServiceAccountJson)
               .CreateScoped(RequiredScopes)
               .CreateWithUser(config.DelegatedUser); // domain-wide delegation impersonation

           return new GmailService(new BaseClientService.Initializer
           {
               HttpClientInitializer = credential,
               ApplicationName = ApplicationName,
           });
       }

       /// <summary>Overload allowing a pre-built service (used by HTTP-fixture tests).</summary>
       public static GmailService Create(BaseClientService.Initializer initializer)
           => new GmailService(initializer);
   }
   ```
5. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailServiceFactoryTests` → expected PASS.
6. - [ ] Commit:
   ```
   git add src/EMaigrator.Connectors.Gmail/GmailServiceFactory.cs src/EMaigrator.Connectors.Gmail/GmailConnectionConfig.cs src/EMaigrator.Connectors.Gmail.Tests/GmailServiceFactoryTests.cs src/EMaigrator.Connectors.Gmail.Tests/TestServiceAccount.cs
   git commit -m "feat(gmail): transient DWD credential factory scoped to mail.google.com only

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
   ```

---

### Task 7: WireMock Gmail fixtures + harness

**Goal:** Add recorded-shape Gmail API JSON fixtures (labels.list, messages.list, messages.get?format=raw, labels.create, messages.import, a 429 quota error) and a `GmailWireMockFixture` test harness that stands up a `WireMock.Net` server and produces a `GmailService` pointed at it via `BaseClientService.Initializer.BaseUri`.

**Files:**
- Create: `src/EMaigrator.Connectors.Gmail.Tests/Fixtures/labels.list.json`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/Fixtures/messages.list.json`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/Fixtures/messages.get.raw.json`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/Fixtures/labels.create.json`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/Fixtures/messages.import.json`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/Fixtures/error.429.json`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/GmailWireMockFixture.cs`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/GmailWireMockFixtureSmokeTests.cs`

**Acceptance Criteria:**
- [ ] Six fixture files exist with valid Gmail-v1-shaped JSON (label list with system + user labels; message-list page with `messages[]` + `nextPageToken`; a `format=raw` get with a base64url `raw`, `internalDate`, `labelIds`; a created-label response; an import response with an id + `labelIds`; a 429 error body with `error.errors[0].reason == "rateLimitExceeded"`).
- [ ] `GmailWireMockFixture` exposes `Server` (WireMock) and `CreateService()` returning a `GmailService` whose `BaseUri` is the mock server URL (no real network).
- [ ] A smoke test maps `GET /gmail/v1/users/me/labels` to the labels fixture and asserts `service.Users.Labels.List("me").ExecuteAsync()` returns the fixture labels — proving the harness round-trips without hitting Google.

**Verify:** `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailWireMockFixtureSmokeTests` → all pass.

**Steps:**
1. - [ ] Write the failing smoke test `src/EMaigrator.Connectors.Gmail.Tests/GmailWireMockFixtureSmokeTests.cs`:
   ```csharp
   using System.Linq;
   using System.Threading.Tasks;
   using FluentAssertions;
   using WireMock.RequestBuilders;
   using WireMock.ResponseBuilders;
   using Xunit;

   namespace EMaigrator.Connectors.Gmail.Tests;

   public class GmailWireMockFixtureSmokeTests
   {
       [Fact]
       public async Task Harness_RoutesLabelsListToFixtureWithoutRealNetwork()
       {
           using var fx = new GmailWireMockFixture();
           fx.Server
             .Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200)
                 .WithHeader("Content-Type", "application/json")
                 .WithBody(fx.Fixture("labels.list.json")));

           using var service = fx.CreateService();
           var result = await service.Users.Labels.List("me").ExecuteAsync();

           result.Labels.Should().NotBeNull();
           result.Labels.Select(l => l.Name).Should().Contain("INBOX");
           result.Labels.Select(l => l.Name).Should().Contain("Work/Clients/Acme");
       }
   }
   ```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailWireMockFixtureSmokeTests` → expected FAIL: `GmailWireMockFixture` and fixtures do not exist (compile error).
3. - [ ] Create the fixtures. `src/EMaigrator.Connectors.Gmail.Tests/Fixtures/labels.list.json`:
   ```json
   {
     "labels": [
       { "id": "INBOX", "name": "INBOX", "type": "system", "messagesTotal": 3 },
       { "id": "SENT", "name": "SENT", "type": "system", "messagesTotal": 1 },
       { "id": "UNREAD", "name": "UNREAD", "type": "system" },
       { "id": "STARRED", "name": "STARRED", "type": "system" },
       { "id": "CHAT", "name": "CHAT", "type": "system" },
       { "id": "CATEGORY_PROMOTIONS", "name": "CATEGORY_PROMOTIONS", "type": "system" },
       { "id": "Label_10", "name": "Work", "type": "user", "messageListVisibility": "show", "labelListVisibility": "labelShow" },
       { "id": "Label_11", "name": "Work/Clients", "type": "user" },
       { "id": "Label_12", "name": "Work/Clients/Acme", "type": "user", "messagesTotal": 2 }
     ]
   }
   ```
   `src/EMaigrator.Connectors.Gmail.Tests/Fixtures/messages.list.json`:
   ```json
   {
     "messages": [
       { "id": "18f0aa11bb22cc01", "threadId": "18f0aa11bb22cc01" },
       { "id": "18f0aa11bb22cc02", "threadId": "18f0aa11bb22cc02" }
     ],
     "resultSizeEstimate": 2
   }
   ```
   `src/EMaigrator.Connectors.Gmail.Tests/Fixtures/messages.get.raw.json` — the `raw` is base64url of a minimal RFC822 message (`Message-ID`, `Subject`, `Date`, `From`, `To`, blank line, body "Hello Acme"):
   ```json
   {
     "id": "18f0aa11bb22cc01",
     "threadId": "18f0aa11bb22cc01",
     "labelIds": ["INBOX", "UNREAD", "Label_12"],
     "internalDate": "1717185600000",
     "sizeEstimate": 482,
     "raw": "TWVzc2FnZS1JRDogPGFjbWUtMDAxQGV4YW1wbGUuY29tPg0KU3ViamVjdDogQWNtZSBxdW90ZQ0KRGF0ZTogRnJpLCAzMSBNYXkgMjAyNCAyMDowMDowMCArMDAwMA0KRnJvbTogc2FsZXNAZXhhbXBsZS5jb20NClRvOiBidXllckBhY21lLmNvbQ0KDQpIZWxsbyBBY21l"
   }
   ```
   `src/EMaigrator.Connectors.Gmail.Tests/Fixtures/labels.create.json`:
   ```json
   { "id": "Label_99", "name": "Migrated/Acme", "type": "user", "messageListVisibility": "show", "labelListVisibility": "labelShow" }
   ```
   `src/EMaigrator.Connectors.Gmail.Tests/Fixtures/messages.import.json`:
   ```json
   { "id": "18f0bb33dd44ee01", "threadId": "18f0bb33dd44ee01", "labelIds": ["Label_99"] }
   ```
   `src/EMaigrator.Connectors.Gmail.Tests/Fixtures/error.429.json`:
   ```json
   {
     "error": {
       "code": 429,
       "message": "User-rate limit exceeded.",
       "errors": [
         { "domain": "usageLimits", "reason": "rateLimitExceeded", "message": "User-rate limit exceeded." }
       ],
       "status": "RESOURCE_EXHAUSTED"
     }
   }
   ```
4. - [ ] Implement the harness `src/EMaigrator.Connectors.Gmail.Tests/GmailWireMockFixture.cs`:
   ```csharp
   using System;
   using System.IO;
   using Google.Apis.Gmail.v1;
   using Google.Apis.Services;
   using WireMock.Server;
   using WireMock.Settings;

   namespace EMaigrator.Connectors.Gmail.Tests;

   /// <summary>
   /// Stands up a local WireMock.Net server and produces a GmailService whose BaseUri points at
   /// it, so all Gmail API calls are served by recorded fixtures with zero real network traffic.
   /// </summary>
   public sealed class GmailWireMockFixture : IDisposable
   {
       public WireMockServer Server { get; }

       public GmailWireMockFixture()
       {
           Server = WireMockServer.Start(new WireMockServerSettings { StartAdminInterface = false });
       }

       public string BaseUrl => Server.Urls[0];

       public string Fixture(string name)
       {
           var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
           return File.ReadAllText(path);
       }

       /// <summary>
       /// A GmailService that talks to the mock server. No credential is attached — WireMock does
       /// not validate Authorization headers, so tests stay offline and credential-free.
       /// </summary>
       public GmailService CreateService()
       {
           return new GmailService(new BaseClientService.Initializer
           {
               BaseUri = $"{BaseUrl}/gmail/v1/",
               ApplicationName = "EMaigrator.Tests",
               // No HttpClientInitializer: avoids any token fetch against real Google endpoints.
           });
       }

       public void Dispose() => Server.Dispose();
   }
   ```
   > Note: `BaseClientService.Initializer.BaseUri` overrides the service endpoint; the Gmail client appends `users/{userId}/...` to it.
5. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailWireMockFixtureSmokeTests` → expected PASS.
6. - [ ] Commit:
   ```
   git add src/EMaigrator.Connectors.Gmail.Tests/Fixtures src/EMaigrator.Connectors.Gmail.Tests/GmailWireMockFixture.cs src/EMaigrator.Connectors.Gmail.Tests/GmailWireMockFixtureSmokeTests.cs
   git commit -m "test(gmail): WireMock harness + recorded Gmail API fixtures

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
   ```

---

### Task 8: GmailSourceProvider — TestConnection, ListFolders, ReadMessages

**Goal:** Implement `GmailSourceProvider : ISourceProvider` binding verbatim to CONTRACTS §2 — `TestConnectionAsync` (labels.list count + All-Mail message estimate), `ListFoldersAsync` (mappable labels → `CanonicalFolder` with special-use), and `ReadMessagesAsync` (messages.list by label → get format=raw → base64url-decoded `OpenContentAsync` stream, `IdentityKey`, `InternalDate`, flags/labels) — all driven by the WireMock harness.

**Files:**
- Create: `src/EMaigrator.Connectors.Gmail/GmailSourceProvider.cs`
- Create: `src/EMaigrator.Connectors.Gmail/GmailRawCodec.cs`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/GmailRawCodecTests.cs`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/GmailSourceProviderTests.cs`

**Acceptance Criteria:**
- [ ] `GmailRawCodec.DecodeBase64Url(raw)` correctly decodes Gmail base64url (`-`/`_`, no padding) to bytes; encode/decode round-trips (unit test, no network).
- [ ] `Id` returns `new ProviderId("gmail")`; `Constraints` returns `GmailConstraints.Default`.
- [ ] `TestConnectionAsync` (against fixtures) returns `Ok == true`, `FolderCount == 7` (the mappable labels in `labels.list.json`, excluding the non-folder state labels CHAT and UNREAD: INBOX, SENT, STARRED, CATEGORY_PROMOTIONS, Work, Work/Clients, Work/Clients/Acme), and `MessageCount` ≥ 0; on a 401 fixture returns `Ok == false` with `ErrorCode == "gmail:401:authError"` and no mailbox address in `RawDetail`.
- [ ] `ListFoldersAsync` returns one `CanonicalFolder` per **mappable** label (the non-folder state labels CHAT and UNREAD excluded), each `Path` derived via `GmailLabelMapper`, `SpecialUse` set to `MessageFlags.None` (Gmail has no canonical special-use beyond labels) and `EstimatedMessageCount` from `messagesTotal`.
- [ ] `ReadMessagesAsync(FolderPath.Parse("Work/Clients/Acme"), ...)` calls `messages.list` with the resolved label id, then `messages.get?format=raw` per id, yielding `CanonicalMessage` with: `IdentityKey` starting `"mid:"` (from the decoded `Message-ID`), `InternalDate` == `2024-05-31T20:00:00Z` (from `internalDate` ms epoch), `Flags` lacking `Seen` (UNREAD present), `Labels` containing `"Work/Clients/Acme"`, and `OpenContentAsync` yielding the decoded RFC822 bytes whose body contains `"Hello Acme"`.
- [ ] `OpenContentAsync` returns a fresh readable `Stream` each call and never persists to disk (asserted: stream is `MemoryStream`).

**Verify:** `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter "FullyQualifiedName~GmailSourceProviderTests|FullyQualifiedName~GmailRawCodecTests"` → all pass.

**Steps:**
1. - [ ] Write the failing tests. `src/EMaigrator.Connectors.Gmail.Tests/GmailRawCodecTests.cs`:
   ```csharp
   using System.Text;
   using EMaigrator.Connectors.Gmail;
   using FluentAssertions;
   using Xunit;

   namespace EMaigrator.Connectors.Gmail.Tests;

   public class GmailRawCodecTests
   {
       [Fact]
       public void EncodeDecode_RoundTrips()
       {
           var original = Encoding.UTF8.GetBytes("Subject: x\r\n\r\nbody with + / = chars");
           var encoded = GmailRawCodec.EncodeBase64Url(original);
           encoded.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
           GmailRawCodec.DecodeBase64Url(encoded).Should().Equal(original);
       }

       [Fact]
       public void DecodeBase64Url_HandlesMissingPadding()
       {
           // "Hi" => base64 "SGk=" => base64url without padding "SGk"
           GmailRawCodec.DecodeBase64Url("SGk").Should().Equal(Encoding.UTF8.GetBytes("Hi"));
       }
   }
   ```
   `src/EMaigrator.Connectors.Gmail.Tests/GmailSourceProviderTests.cs`:
   ```csharp
   using System;
   using System.Collections.Generic;
   using System.IO;
   using System.Linq;
   using System.Text;
   using System.Threading;
   using System.Threading.Tasks;
   using EMaigrator.Connectors.Gmail;
   using EMaigrator.Core.Model;
   using FluentAssertions;
   using WireMock.RequestBuilders;
   using WireMock.ResponseBuilders;
   using Xunit;

   namespace EMaigrator.Connectors.Gmail.Tests;

   public class GmailSourceProviderTests
   {
       private static void StubLabels(GmailWireMockFixture fx) =>
           fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200)
                 .WithHeader("Content-Type", "application/json").WithBody(fx.Fixture("labels.list.json")));

       private static void StubMessagesList(GmailWireMockFixture fx) =>
           fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200)
                 .WithHeader("Content-Type", "application/json").WithBody(fx.Fixture("messages.list.json")));

       private static void StubMessageGet(GmailWireMockFixture fx) =>
           fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages/*").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200)
                 .WithHeader("Content-Type", "application/json").WithBody(fx.Fixture("messages.get.raw.json")));

       [Fact]
       public void Id_And_Constraints_AreGmail()
       {
           using var fx = new GmailWireMockFixture();
           var sut = new GmailSourceProvider(fx.CreateService(), "me");
           sut.Id.Should().Be(new ProviderId("gmail"));
           sut.Constraints.Should().BeSameAs(GmailConstraints.Default);
       }

       [Fact]
       public async Task TestConnectionAsync_ReturnsOkAndFolderCount()
       {
           using var fx = new GmailWireMockFixture();
           StubLabels(fx);
           var sut = new GmailSourceProvider(fx.CreateService(), "me");

           var result = await sut.TestConnectionAsync(CancellationToken.None);

           result.Ok.Should().BeTrue();
           result.FolderCount.Should().Be(7); // mappable labels (CHAT + UNREAD excluded): INBOX,SENT,STARRED,CATEGORY_PROMOTIONS,Work,Work/Clients,Work/Clients/Acme
       }

       [Fact]
       public async Task TestConnectionAsync_On401_ReturnsErrorCodeWithoutMailbox()
       {
           using var fx = new GmailWireMockFixture();
           fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(401)
                 .WithHeader("Content-Type", "application/json")
                 .WithBody("{\"error\":{\"code\":401,\"message\":\"Invalid Credentials for victim@example.com\",\"errors\":[{\"reason\":\"authError\",\"message\":\"Invalid Credentials\"}]}}"));
           var sut = new GmailSourceProvider(fx.CreateService(), "me");

           var result = await sut.TestConnectionAsync(CancellationToken.None);

           result.Ok.Should().BeFalse();
           result.ErrorCode.Should().Be("gmail:401:authError");
           (result.RawDetail ?? "").Should().NotContain("@");
       }

       [Fact]
       public async Task ListFoldersAsync_MapsMappableLabels()
       {
           using var fx = new GmailWireMockFixture();
           StubLabels(fx);
           var sut = new GmailSourceProvider(fx.CreateService(), "me");

           var folders = await sut.ListFoldersAsync(CancellationToken.None);

           folders.Select(f => f.Path.ToString()).Should().NotContain("CHAT");
           folders.Select(f => f.Path.ToString()).Should().Contain("Work/Clients/Acme");
           folders.Should().HaveCount(7);
       }

       [Fact]
       public async Task ReadMessagesAsync_YieldsCanonicalMessageFromRaw()
       {
           using var fx = new GmailWireMockFixture();
           StubLabels(fx);
           StubMessagesList(fx);
           StubMessageGet(fx);
           var sut = new GmailSourceProvider(fx.CreateService(), "me");

           var msgs = new List<CanonicalMessage>();
           await foreach (var m in sut.ReadMessagesAsync(FolderPath.Parse("Work/Clients/Acme"), new(), CancellationToken.None))
               msgs.Add(m);

           msgs.Should().NotBeEmpty();
           var first = msgs[0];
           first.IdentityKey.Should().StartWith("mid:");
           first.InternalDate.Should().Be(DateTimeOffset.Parse("2024-05-31T20:00:00Z"));
           first.Flags.Should().NotHaveFlag(MessageFlags.Seen);
           first.Labels.Should().Contain("Work/Clients/Acme");

           await using var stream = await first.OpenContentAsync(CancellationToken.None);
           stream.Should().BeOfType<MemoryStream>();
           using var reader = new StreamReader(stream, Encoding.UTF8);
           var rfc822 = await reader.ReadToEndAsync();
           rfc822.Should().Contain("Hello Acme");
       }
   }
   ```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter "FullyQualifiedName~GmailSourceProviderTests|FullyQualifiedName~GmailRawCodecTests"` → expected FAIL: `GmailRawCodec`/`GmailSourceProvider` do not exist (compile error).
3. - [ ] Implement `src/EMaigrator.Connectors.Gmail/GmailRawCodec.cs`:
   ```csharp
   using System;

   namespace EMaigrator.Connectors.Gmail;

   /// <summary>Encodes/decodes Gmail's URL-safe base64 (RFC 4648 §5, no padding) used by format=raw.</summary>
   public static class GmailRawCodec
   {
       public static byte[] DecodeBase64Url(string value)
       {
           var s = value.Replace('-', '+').Replace('_', '/');
           switch (s.Length % 4)
           {
               case 2: s += "=="; break;
               case 3: s += "="; break;
           }
           return Convert.FromBase64String(s);
       }

       public static string EncodeBase64Url(byte[] bytes)
           => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
   }
   ```
   Implement `src/EMaigrator.Connectors.Gmail/GmailSourceProvider.cs`:
   ```csharp
   using System;
   using System.Collections.Generic;
   using System.IO;
   using System.Linq;
   using System.Runtime.CompilerServices;
   using System.Text;
   using System.Threading;
   using System.Threading.Tasks;
   using EMaigrator.Core.Abstractions;
   using EMaigrator.Core.Idempotency;
   using EMaigrator.Core.Model;
   using Google;
   using Google.Apis.Gmail.v1;
   using Google.Apis.Gmail.v1.Data;
   using GmailMessage = Google.Apis.Gmail.v1.Data.Message;

   namespace EMaigrator.Connectors.Gmail;

   /// <summary>
   /// Gmail source provider (CONTRACTS §2). Reads labels as folders and messages as raw RFC822,
   /// streaming bodies through memory only (DESIGN.md §10).
   /// </summary>
   public sealed class GmailSourceProvider : ISourceProvider
   {
       private readonly GmailService _service;
       private readonly string _userId;

       public GmailSourceProvider(GmailService service, string userId)
       {
           _service = service ?? throw new ArgumentNullException(nameof(service));
           _userId = string.IsNullOrWhiteSpace(userId) ? "me" : userId;
       }

       public ProviderId Id => new("gmail");
       public ProviderConstraints Constraints => GmailConstraints.Default;

       public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct)
       {
           try
           {
               var labels = await ListMappableLabelsAsync(ct);
               long messageCount = labels.Sum(l => l.MessagesTotal ?? 0);
               return new ConnectionTestResult(true, labels.Count, messageCount);
           }
           catch (Exception ex)
           {
               // RawDetail is a generic, credential/mailbox-free diagnostic; the precise reason
               // lives in the normalized signature, never the impersonated address.
               return new ConnectionTestResult(false, 0, 0, GmailErrorNormalizer.Normalize(ex), "Gmail connection failed.");
           }
       }

       public async Task<IReadOnlyList<CanonicalFolder>> ListFoldersAsync(CancellationToken ct)
       {
           var labels = await ListMappableLabelsAsync(ct);
           return labels
               .Select(l => new CanonicalFolder(
                   GmailLabelMapper.LabelNameToFolderPath(l.Name),
                   l.MessagesTotal ?? 0,
                   MessageFlags.None))
               .ToList();
       }

       public async IAsyncEnumerable<CanonicalMessage> ReadMessagesAsync(
           FolderPath folder, ReadOptions options, [EnumeratorCancellation] CancellationToken ct)
       {
           var labelName = GmailLabelMapper.FolderPathToLabelName(folder);
           var labelMap = await BuildLabelMapAsync(ct);
           var labelId = ResolveLabelId(labelName, labelMap);

           string? pageToken = null;
           do
           {
               var listReq = _service.Users.Messages.List(_userId);
               if (labelId is not null) listReq.LabelIds = new[] { labelId };
               listReq.Q = BuildQuery(options);
               listReq.PageToken = pageToken;
               var page = await listReq.ExecuteAsync(ct);
               pageToken = page.NextPageToken;

               if (page.Messages is null) yield break;

               foreach (var stub in page.Messages)
               {
                   ct.ThrowIfCancellationRequested();
                   var getReq = _service.Users.Messages.Get(_userId, stub.Id);
                   getReq.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Raw;
                   var full = await getReq.ExecuteAsync(ct);
                   yield return ToCanonical(full, labelMap);
               }
           } while (!string.IsNullOrEmpty(pageToken));
       }

       private static string? BuildQuery(ReadOptions options)
       {
           var parts = new List<string>();
           if (options.Since is { } since) parts.Add($"after:{since.ToUnixTimeSeconds()}");
           if (options.Before is { } before) parts.Add($"before:{before.ToUnixTimeSeconds()}");
           return parts.Count == 0 ? null : string.Join(' ', parts);
       }

       private CanonicalMessage ToCanonical(GmailMessage m, IReadOnlyDictionary<string, string> labelMap)
       {
           var rawBytes = GmailRawCodec.DecodeBase64Url(m.Raw);
           var rfc822 = Encoding.UTF8.GetString(rawBytes);
           var messageId = ExtractHeader(rfc822, "Message-ID");
           var subject = ExtractHeader(rfc822, "Subject");

           var labelIds = (IReadOnlyCollection<string>)(m.LabelIds ?? new List<string>());
           var flags = GmailFlagMapper.ToFlags(labelIds);
           var labels = GmailFlagMapper.ToCanonicalLabels(labelIds, labelMap);

           var internalDate = m.InternalDate is { } ms
               ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
               : DateTimeOffset.UnixEpoch;

           var bodySha = Convert.ToHexString(
               System.Security.Cryptography.SHA256.HashData(ExtractDecodedBody(rfc822))).ToLowerInvariant();

           var identityKey = IdentityKey.Compute(new MessageIdentityInput
           {
               MessageId = messageId,
               Subject = subject,
               Date = internalDate,
               DecodedBodySha256Hex = bodySha,
           });

           // Capture bytes for the closure; never written to disk.
           var captured = rawBytes;
           return new CanonicalMessage
           {
               IdentityKey = identityKey,
               MessageId = messageId,
               InternalDate = internalDate,
               Flags = flags,
               Labels = labels,
               SizeBytes = m.SizeEstimate ?? captured.LongLength,
               Subject = subject,
               OpenContentAsync = _ => Task.FromResult<Stream>(new MemoryStream(captured, writable: false)),
           };
       }

       private static byte[] ExtractDecodedBody(string rfc822)
       {
           var idx = rfc822.IndexOf("\r\n\r\n", StringComparison.Ordinal);
           if (idx < 0) idx = rfc822.IndexOf("\n\n", StringComparison.Ordinal);
           var body = idx < 0 ? "" : rfc822[(idx)..].TrimStart('\r', '\n');
           return Encoding.UTF8.GetBytes(body);
       }

       private static string? ExtractHeader(string rfc822, string name)
       {
           foreach (var line in rfc822.Split('\n'))
           {
               if (line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
                   return line[(name.Length + 1)..].Trim().TrimEnd('\r');
               if (line.Length == 0 || line == "\r") break; // end of headers
           }
           return null;
       }

       private async Task<IReadOnlyList<Label>> ListMappableLabelsAsync(CancellationToken ct)
       {
           var resp = await _service.Users.Labels.List(_userId).ExecuteAsync(ct);
           return (resp.Labels ?? new List<Label>())
               .Where(l => l.Name is not null && GmailLabelMapper.IsMappableLabel(l.Name))
               .ToList();
       }

       private async Task<IReadOnlyDictionary<string, string>> BuildLabelMapAsync(CancellationToken ct)
       {
           var resp = await _service.Users.Labels.List(_userId).ExecuteAsync(ct);
           return (resp.Labels ?? new List<Label>())
               .Where(l => l.Id is not null && l.Name is not null)
               .ToDictionary(l => l.Id!, l => l.Name!);
       }

       private static string? ResolveLabelId(string labelName, IReadOnlyDictionary<string, string> labelMap)
       {
           if (GmailLabelMapper.IsSystemLabel(labelName))
               return labelName; // system label ids equal their names
           return labelMap.FirstOrDefault(kv => kv.Value == labelName).Key;
       }

       public ValueTask DisposeAsync()
       {
           _service.Dispose();
           return ValueTask.CompletedTask;
       }
   }
   ```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter "FullyQualifiedName~GmailSourceProviderTests|FullyQualifiedName~GmailRawCodecTests"` → expected PASS.
5. - [ ] Commit:
   ```
   git add src/EMaigrator.Connectors.Gmail/GmailSourceProvider.cs src/EMaigrator.Connectors.Gmail/GmailRawCodec.cs src/EMaigrator.Connectors.Gmail.Tests/GmailRawCodecTests.cs src/EMaigrator.Connectors.Gmail.Tests/GmailSourceProviderTests.cs
   git commit -m "feat(gmail): GmailSourceProvider (test-connection, list-folders, read raw messages)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
   ```

---

### Task 9: GmailDestinationProvider — EnsureFolder, WriteMessage, ExistsByMessageId

**Goal:** Implement `GmailDestinationProvider : IDestinationProvider` per CONTRACTS §2 — `EnsureFolderAsync` (idempotent labels.create), `WriteMessageAsync` (messages.import with `internalDateSource=dateHeader`, applying the destination label + preserving canonical labels + SEEN), and `ExistsByMessageIdAsync` (messages.list `q=rfc822msgid:<id>`), driven by the WireMock harness.

**Files:**
- Create: `src/EMaigrator.Connectors.Gmail/GmailDestinationProvider.cs`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/GmailDestinationProviderTests.cs`

**Acceptance Criteria:**
- [ ] `Id`/`Constraints` are Gmail's (as Task 8).
- [ ] `EnsureFolderAsync(FolderPath.Parse("Migrated/Acme"))` calls `labels.create` once when absent, and is a no-op (no second create) when the label already exists in the label list (idempotent).
- [ ] `WriteMessageAsync` posts to `messages.import` with the base64url-encoded raw content (from `OpenContentAsync`), `internalDateSource=dateHeader`, and `labelIds` including the destination label id and any preserved canonical labels; returns `WriteResult(Written: true, DestMessageId: <id>)`.
- [ ] When the message lacks `Seen`, the import request includes `UNREAD` in `labelIds`; when `Seen` is set, `UNREAD` is omitted.
- [ ] On a 429 fixture, `WriteMessageAsync` returns `WriteResult(Written: false, ErrorCode: "gmail:429:rateLimitExceeded")` (does not throw; lets the worker back off).
- [ ] `ExistsByMessageIdAsync(folder, "<acme-001@example.com>")` issues `messages.list?q=rfc822msgid:<id>` and returns `true` when the list is non-empty, `false` when empty.

**Verify:** `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailDestinationProviderTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Gmail.Tests/GmailDestinationProviderTests.cs`:
   ```csharp
   using System;
   using System.Collections.Generic;
   using System.IO;
   using System.Text;
   using System.Threading;
   using System.Threading.Tasks;
   using EMaigrator.Connectors.Gmail;
   using EMaigrator.Core.Model;
   using FluentAssertions;
   using WireMock.RequestBuilders;
   using WireMock.ResponseBuilders;
   using Xunit;

   namespace EMaigrator.Connectors.Gmail.Tests;

   public class GmailDestinationProviderTests
   {
       private static void StubLabels(GmailWireMockFixture fx, string body) =>
           fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200)
                 .WithHeader("Content-Type", "application/json").WithBody(body));

       private static CanonicalMessage Msg(MessageFlags flags = MessageFlags.None, IReadOnlyList<string>? labels = null)
       {
           var raw = Encoding.UTF8.GetBytes("Message-ID: <acme-001@example.com>\r\nSubject: x\r\n\r\nbody");
           return new CanonicalMessage
           {
               IdentityKey = "mid:<acme-001@example.com>",
               MessageId = "<acme-001@example.com>",
               InternalDate = DateTimeOffset.Parse("2024-05-31T20:00:00Z"),
               Flags = flags,
               Labels = labels ?? Array.Empty<string>(),
               OpenContentAsync = _ => Task.FromResult<Stream>(new MemoryStream(raw, writable: false)),
           };
       }

       [Fact]
       public async Task EnsureFolderAsync_CreatesLabelWhenAbsent()
       {
           using var fx = new GmailWireMockFixture();
           StubLabels(fx, fx.Fixture("labels.list.json"));
           fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingPost())
             .RespondWith(Response.Create().WithStatusCode(200)
                 .WithHeader("Content-Type", "application/json").WithBody(fx.Fixture("labels.create.json")));
           var sut = new GmailDestinationProvider(fx.CreateService(), "me");

           await sut.EnsureFolderAsync(FolderPath.Parse("Migrated/Acme"), CancellationToken.None);

           fx.Server.LogEntries.Should().Contain(e =>
               e.RequestMessage.Method == "POST" && e.RequestMessage.Path == "/gmail/v1/users/me/labels");
       }

       [Fact]
       public async Task EnsureFolderAsync_NoOpWhenLabelExists()
       {
           using var fx = new GmailWireMockFixture();
           StubLabels(fx, fx.Fixture("labels.list.json"));
           var sut = new GmailDestinationProvider(fx.CreateService(), "me");

           await sut.EnsureFolderAsync(FolderPath.Parse("Work/Clients/Acme"), CancellationToken.None);

           fx.Server.LogEntries.Should().NotContain(e => e.RequestMessage.Method == "POST");
       }

       [Fact]
       public async Task WriteMessageAsync_ImportsRawWithDateHeaderAndLabels()
       {
           using var fx = new GmailWireMockFixture();
           StubLabels(fx, fx.Fixture("labels.list.json"));
           fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages/import").UsingPost())
             .RespondWith(Response.Create().WithStatusCode(200)
                 .WithHeader("Content-Type", "application/json").WithBody(fx.Fixture("messages.import.json")));
           var sut = new GmailDestinationProvider(fx.CreateService(), "me");

           var result = await sut.WriteMessageAsync(FolderPath.Parse("Work/Clients/Acme"), Msg(MessageFlags.Seen), CancellationToken.None);

           result.Written.Should().BeTrue();
           result.DestMessageId.Should().Be("18f0bb33dd44ee01");
           var import = System.Array.Find(fx.Server.LogEntries.ToArray(),
               e => e.RequestMessage.Path == "/gmail/v1/users/me/messages/import");
           import.Should().NotBeNull();
           import!.RequestMessage.RawQuery.Should().Contain("internalDateSource=dateHeader");
       }

       [Fact]
       public async Task WriteMessageAsync_UnseenAddsUnreadLabel()
       {
           using var fx = new GmailWireMockFixture();
           StubLabels(fx, fx.Fixture("labels.list.json"));
           fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages/import").UsingPost())
             .RespondWith(Response.Create().WithStatusCode(200)
                 .WithHeader("Content-Type", "application/json").WithBody(fx.Fixture("messages.import.json")));
           var sut = new GmailDestinationProvider(fx.CreateService(), "me");

           await sut.WriteMessageAsync(FolderPath.Parse("Work/Clients/Acme"), Msg(MessageFlags.None), CancellationToken.None);

           var import = System.Array.Find(fx.Server.LogEntries.ToArray(),
               e => e.RequestMessage.Path == "/gmail/v1/users/me/messages/import");
           import!.RequestMessage.Body.Should().Contain("UNREAD");
       }

       [Fact]
       public async Task WriteMessageAsync_On429_ReturnsErrorCodeWithoutThrowing()
       {
           using var fx = new GmailWireMockFixture();
           StubLabels(fx, fx.Fixture("labels.list.json"));
           fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages/import").UsingPost())
             .RespondWith(Response.Create().WithStatusCode(429)
                 .WithHeader("Content-Type", "application/json").WithBody(fx.Fixture("error.429.json")));
           var sut = new GmailDestinationProvider(fx.CreateService(), "me");

           var result = await sut.WriteMessageAsync(FolderPath.Parse("Work/Clients/Acme"), Msg(MessageFlags.Seen), CancellationToken.None);

           result.Written.Should().BeFalse();
           result.ErrorCode.Should().Be("gmail:429:rateLimitExceeded");
       }

       [Fact]
       public async Task ExistsByMessageIdAsync_TrueWhenSearchNonEmpty()
       {
           using var fx = new GmailWireMockFixture();
           fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200)
                 .WithHeader("Content-Type", "application/json").WithBody(fx.Fixture("messages.list.json")));
           var sut = new GmailDestinationProvider(fx.CreateService(), "me");

           var exists = await sut.ExistsByMessageIdAsync(FolderPath.Parse("Work"), "<acme-001@example.com>", CancellationToken.None);

           exists.Should().BeTrue();
           var search = System.Array.Find(fx.Server.LogEntries.ToArray(),
               e => e.RequestMessage.Path == "/gmail/v1/users/me/messages");
           search!.RequestMessage.RawQuery.Should().Contain("rfc822msgid");
       }

       [Fact]
       public async Task ExistsByMessageIdAsync_FalseWhenSearchEmpty()
       {
           using var fx = new GmailWireMockFixture();
           fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200)
                 .WithHeader("Content-Type", "application/json").WithBody("{\"resultSizeEstimate\":0}"));
           var sut = new GmailDestinationProvider(fx.CreateService(), "me");

           (await sut.ExistsByMessageIdAsync(FolderPath.Parse("Work"), "<missing@example.com>", CancellationToken.None))
               .Should().BeFalse();
       }
   }
   ```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailDestinationProviderTests` → expected FAIL: `GmailDestinationProvider` does not exist (compile error).
3. - [ ] Implement `src/EMaigrator.Connectors.Gmail/GmailDestinationProvider.cs`:
   ```csharp
   using System;
   using System.Collections.Generic;
   using System.IO;
   using System.Linq;
   using System.Threading;
   using System.Threading.Tasks;
   using EMaigrator.Core.Abstractions;
   using EMaigrator.Core.Model;
   using Google;
   using Google.Apis.Gmail.v1;
   using Google.Apis.Gmail.v1.Data;
   using GmailMessage = Google.Apis.Gmail.v1.Data.Message;

   namespace EMaigrator.Connectors.Gmail;

   /// <summary>
   /// Gmail destination provider (CONTRACTS §2). Creates labels for folders, imports raw RFC822
   /// preserving the original date and read/label state, and supports rfc822msgid dedup.
   /// </summary>
   public sealed class GmailDestinationProvider : IDestinationProvider
   {
       private readonly GmailService _service;
       private readonly string _userId;

       public GmailDestinationProvider(GmailService service, string userId)
       {
           _service = service ?? throw new ArgumentNullException(nameof(service));
           _userId = string.IsNullOrWhiteSpace(userId) ? "me" : userId;
       }

       public ProviderId Id => new("gmail");
       public ProviderConstraints Constraints => GmailConstraints.Default;

       public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct)
       {
           try
           {
               var resp = await _service.Users.Labels.List(_userId).ExecuteAsync(ct);
               var count = resp.Labels?.Count(l => l.Name is not null && GmailLabelMapper.IsMappableLabel(l.Name)) ?? 0;
               return new ConnectionTestResult(true, count, 0);
           }
           catch (Exception ex)
           {
               return new ConnectionTestResult(false, 0, 0, GmailErrorNormalizer.Normalize(ex), "Gmail connection failed.");
           }
       }

       public async Task EnsureFolderAsync(FolderPath folder, CancellationToken ct)
       {
           var labelName = GmailLabelMapper.FolderPathToLabelName(folder);
           if (GmailLabelMapper.IsSystemLabel(labelName))
               return; // system labels always exist

           var existing = await GetLabelIdAsync(labelName, ct);
           if (existing is not null)
               return; // idempotent: already present

           await _service.Users.Labels.Create(new Label
           {
               Name = labelName,
               MessageListVisibility = "show",
               LabelListVisibility = "labelShow",
           }, _userId).ExecuteAsync(ct);
       }

       public async Task<WriteResult> WriteMessageAsync(FolderPath folder, CanonicalMessage message, CancellationToken ct)
       {
           try
           {
               byte[] raw;
               await using (var stream = await message.OpenContentAsync(ct))
               using (var ms = new MemoryStream())
               {
                   await stream.CopyToAsync(ms, ct);
                   raw = ms.ToArray();
               }

               var labelIds = await ResolveDestinationLabelIdsAsync(folder, message, ct);

               var gmsg = new GmailMessage
               {
                   Raw = GmailRawCodec.EncodeBase64Url(raw),
                   LabelIds = labelIds,
               };

               var import = _service.Users.Messages.Import(gmsg, _userId);
               import.InternalDateSource =
                   UsersResource.MessagesResource.ImportRequest.InternalDateSourceEnum.DateHeader;
               import.NeverMarkSpam = true;       // a migration must not silently spam-file
               import.ProcessForCalendar = false;

               var result = await import.ExecuteAsync(ct);
               return new WriteResult(true, result.Id);
           }
           catch (Exception ex)
           {
               return new WriteResult(false, null, GmailErrorNormalizer.Normalize(ex));
           }
       }

       public async Task<bool> ExistsByMessageIdAsync(FolderPath folder, string messageId, CancellationToken ct)
       {
           var id = messageId.Trim().Trim('<', '>');
           var req = _service.Users.Messages.List(_userId);
           req.Q = $"rfc822msgid:{id}";
           var resp = await req.ExecuteAsync(ct);
           return resp.Messages is { Count: > 0 };
       }

       private async Task<IList<string>> ResolveDestinationLabelIdsAsync(
           FolderPath folder, CanonicalMessage message, CancellationToken ct)
       {
           var ids = new List<string>();
           var folderLabel = GmailLabelMapper.FolderPathToLabelName(folder);
           var folderId = GmailLabelMapper.IsSystemLabel(folderLabel)
               ? folderLabel
               : await GetLabelIdAsync(folderLabel, ct);
           if (folderId is not null) ids.Add(folderId);

           // Preserve canonical labels (resolve/auto-create as needed).
           foreach (var name in message.Labels)
           {
               if (GmailLabelMapper.IsSystemLabel(name)) { ids.Add(name); continue; }
               var lid = await GetLabelIdAsync(name, ct);
               if (lid is not null && !ids.Contains(lid)) ids.Add(lid);
           }

           // Read-state: unseen => keep UNREAD.
           if (!message.Flags.HasFlag(MessageFlags.Seen) && !ids.Contains("UNREAD"))
               ids.Add("UNREAD");
           if (message.Flags.HasFlag(MessageFlags.Flagged) && !ids.Contains("STARRED"))
               ids.Add("STARRED");

           return ids;
       }

       private async Task<string?> GetLabelIdAsync(string labelName, CancellationToken ct)
       {
           var resp = await _service.Users.Labels.List(_userId).ExecuteAsync(ct);
           return resp.Labels?
               .FirstOrDefault(l => string.Equals(l.Name, labelName, StringComparison.Ordinal))?.Id;
       }

       public ValueTask DisposeAsync()
       {
           _service.Dispose();
           return ValueTask.CompletedTask;
       }
   }
   ```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailDestinationProviderTests` → expected PASS.
5. - [ ] Commit:
   ```
   git add src/EMaigrator.Connectors.Gmail/GmailDestinationProvider.cs src/EMaigrator.Connectors.Gmail.Tests/GmailDestinationProviderTests.cs
   git commit -m "feat(gmail): GmailDestinationProvider (ensure-label, import-with-date, dedup)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
   ```

---

### Task 10: GmailProviderPlugin + DI registration

**Goal:** Implement `GmailProviderPlugin : IProviderPlugin` (declares `ProviderId("gmail")`, `SupportedAuth = [GmailServiceAccountDwd]`, `CanBeSource`/`CanBeDestination` true, and `CreateSource`/`CreateDestination` building providers via `GmailConnectionConfig` + `GmailServiceFactory`) and the `AddGmailConnector()` DI extension (CONTRACTS §8 naming).

**Files:**
- Create: `src/EMaigrator.Connectors.Gmail/GmailProviderPlugin.cs`
- Create: `src/EMaigrator.Connectors.Gmail/ServiceCollectionExtensions.cs`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/GmailProviderPluginTests.cs`

**Acceptance Criteria:**
- [ ] `GmailProviderPlugin.Id == new ProviderId("gmail")`; `SupportedAuth` equals `[AuthMethod.GmailServiceAccountDwd]`; `CanBeSource && CanBeDestination`.
- [ ] `CreateSource(descriptor, secrets)` returns a `GmailSourceProvider` whose `Id`/`Constraints` are Gmail's, using `Settings["accountEmail"]` as the impersonated user.
- [ ] `CreateDestination(descriptor, secrets)` returns a `GmailDestinationProvider`.
- [ ] `CreateSource` with a descriptor missing `accountEmail` throws `ArgumentException` (delegated via `GmailConnectionConfig.FromDescriptor`).
- [ ] `AddGmailConnector()` registers `IProviderPlugin` → `GmailProviderPlugin` in the DI container, resolvable as `IEnumerable<IProviderPlugin>` containing exactly one Gmail plugin.

**Verify:** `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailProviderPluginTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Gmail.Tests/GmailProviderPluginTests.cs`:
   ```csharp
   using System;
   using System.Collections.Generic;
   using System.Linq;
   using EMaigrator.Connectors.Gmail;
   using EMaigrator.Core.Abstractions;
   using EMaigrator.Core.Model;
   using FluentAssertions;
   using Microsoft.Extensions.DependencyInjection;
   using Xunit;

   namespace EMaigrator.Connectors.Gmail.Tests;

   public class GmailProviderPluginTests
   {
       private static ConnectionDescriptor Descriptor(string? email = "target@example.com") => new()
       {
           Provider = new ProviderId("gmail"),
           Auth = AuthMethod.GmailServiceAccountDwd,
           Settings = new Dictionary<string, string> { ["accountEmail"] = email ?? "" },
       };

       private static SecretBundle Secrets() =>
           new(new Dictionary<string, string> { ["serviceAccountJson"] = TestServiceAccount.Json });

       [Fact]
       public void Metadata_DeclaresGmailDwdSourceAndDestination()
       {
           var plugin = new GmailProviderPlugin();
           plugin.Id.Should().Be(new ProviderId("gmail"));
           plugin.SupportedAuth.Should().Equal(new[] { AuthMethod.GmailServiceAccountDwd });
           plugin.CanBeSource.Should().BeTrue();
           plugin.CanBeDestination.Should().BeTrue();
       }

       [Fact]
       public void CreateSource_ReturnsGmailSourceProvider()
       {
           var plugin = new GmailProviderPlugin();
           var src = plugin.CreateSource(Descriptor(), Secrets());
           src.Should().BeOfType<GmailSourceProvider>();
           src.Id.Should().Be(new ProviderId("gmail"));
       }

       [Fact]
       public void CreateDestination_ReturnsGmailDestinationProvider()
       {
           var plugin = new GmailProviderPlugin();
           var dst = plugin.CreateDestination(Descriptor(), Secrets());
           dst.Should().BeOfType<GmailDestinationProvider>();
       }

       [Fact]
       public void CreateSource_MissingEmail_Throws()
       {
           var plugin = new GmailProviderPlugin();
           var act = () => plugin.CreateSource(Descriptor(email: ""), Secrets());
           act.Should().Throw<ArgumentException>();
       }

       [Fact]
       public void AddGmailConnector_RegistersSinglePlugin()
       {
           var sp = new ServiceCollection().AddGmailConnector().BuildServiceProvider();
           var plugins = sp.GetServices<IProviderPlugin>().ToList();
           plugins.Should().ContainSingle(p => p.Id == new ProviderId("gmail"));
       }
   }
   ```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailProviderPluginTests` → expected FAIL: `GmailProviderPlugin`/`AddGmailConnector` do not exist (compile error). Add `Microsoft.Extensions.DependencyInjection.Abstractions` to the Gmail project if not transitively present.
3. - [ ] Add the DI package to `src/EMaigrator.Connectors.Gmail/EMaigrator.Connectors.Gmail.csproj` (inside the existing package `ItemGroup`):
   ```xml
       <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
   ```
   Implement `src/EMaigrator.Connectors.Gmail/GmailProviderPlugin.cs`:
   ```csharp
   using System.Collections.Generic;
   using EMaigrator.Core.Abstractions;
   using EMaigrator.Core.Model;

   namespace EMaigrator.Connectors.Gmail;

   /// <summary>
   /// DI-discovered descriptor for the Gmail connector (CONTRACTS §2). v1 supports BYO
   /// service-account + domain-wide delegation only (DESIGN.md §11).
   /// </summary>
   public sealed class GmailProviderPlugin : IProviderPlugin
   {
       public ProviderId Id => new("gmail");

       public IReadOnlyCollection<AuthMethod> SupportedAuth { get; } =
           new[] { AuthMethod.GmailServiceAccountDwd };

       public bool CanBeSource => true;
       public bool CanBeDestination => true;

       public ISourceProvider CreateSource(ConnectionDescriptor descriptor, SecretBundle secrets)
       {
           var config = GmailConnectionConfig.FromDescriptor(descriptor, secrets);
           var service = GmailServiceFactory.Create(config);
           return new GmailSourceProvider(service, config.DelegatedUser);
       }

       public IDestinationProvider CreateDestination(ConnectionDescriptor descriptor, SecretBundle secrets)
       {
           var config = GmailConnectionConfig.FromDescriptor(descriptor, secrets);
           var service = GmailServiceFactory.Create(config);
           return new GmailDestinationProvider(service, config.DelegatedUser);
       }
   }
   ```
   Implement `src/EMaigrator.Connectors.Gmail/ServiceCollectionExtensions.cs`:
   ```csharp
   using EMaigrator.Core.Abstractions;
   using Microsoft.Extensions.DependencyInjection;
   using Microsoft.Extensions.DependencyInjection.Extensions;

   namespace EMaigrator.Connectors.Gmail;

   public static class ServiceCollectionExtensions
   {
       /// <summary>Registers the Gmail connector's single <see cref="IProviderPlugin"/>.</summary>
       public static IServiceCollection AddGmailConnector(this IServiceCollection services)
       {
           services.TryAddEnumerable(
               ServiceDescriptor.Singleton<IProviderPlugin, GmailProviderPlugin>());
           return services;
       }
   }
   ```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailProviderPluginTests` → expected PASS.
5. - [ ] Commit:
   ```
   git add src/EMaigrator.Connectors.Gmail/GmailProviderPlugin.cs src/EMaigrator.Connectors.Gmail/ServiceCollectionExtensions.cs src/EMaigrator.Connectors.Gmail/EMaigrator.Connectors.Gmail.csproj src/EMaigrator.Connectors.Gmail.Tests/GmailProviderPluginTests.cs
   git commit -m "feat(gmail): GmailProviderPlugin + AddGmailConnector DI registration

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
   ```

---

### Task 11: Contract conformance tests (ISourceProvider / IDestinationProvider)

**Goal:** Add provider-boundary contract tests proving `GmailSourceProvider` and `GmailDestinationProvider` honor the CONTRACTS §2 interface semantics end-to-end against fixtures (round-trip: read a raw message from the source, write it via the destination's import, and confirm `ExistsByMessageIdAsync` would find it), and that both are correctly typed as `IAsyncDisposable`.

**Files:**
- Create: `src/EMaigrator.Connectors.Gmail.Tests/GmailContractConformanceTests.cs`

**Acceptance Criteria:**
- [ ] `GmailSourceProvider` is assignable to `ISourceProvider` and `IAsyncDisposable`; `GmailDestinationProvider` to `IDestinationProvider` and `IAsyncDisposable` (compile + `Assignable` assertions).
- [ ] A round-trip test: read the fixture message via the source, pass the resulting `CanonicalMessage` straight into the destination's `WriteMessageAsync` (streaming the same `OpenContentAsync`), and assert `WriteResult.Written == true` and the import body's decoded raw contains the original `Message-ID` (`<acme-001@example.com>`) — proving the streaming pass-through preserves identity without persistence.
- [ ] `DisposeAsync` on both providers completes without throwing.
- [ ] The source's emitted `CanonicalMessage.OpenContentAsync` can be invoked twice and yields equal bytes both times (no single-use stream that would break retries).

**Verify:** `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailContractConformanceTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Gmail.Tests/GmailContractConformanceTests.cs`:
   ```csharp
   using System;
   using System.IO;
   using System.Text;
   using System.Threading;
   using System.Threading.Tasks;
   using EMaigrator.Connectors.Gmail;
   using EMaigrator.Core.Abstractions;
   using EMaigrator.Core.Model;
   using FluentAssertions;
   using WireMock.RequestBuilders;
   using WireMock.ResponseBuilders;
   using Xunit;

   namespace EMaigrator.Connectors.Gmail.Tests;

   public class GmailContractConformanceTests
   {
       [Fact]
       public void Providers_ImplementContractInterfaces()
       {
           typeof(ISourceProvider).IsAssignableFrom(typeof(GmailSourceProvider)).Should().BeTrue();
           typeof(IAsyncDisposable).IsAssignableFrom(typeof(GmailSourceProvider)).Should().BeTrue();
           typeof(IDestinationProvider).IsAssignableFrom(typeof(GmailDestinationProvider)).Should().BeTrue();
           typeof(IAsyncDisposable).IsAssignableFrom(typeof(GmailDestinationProvider)).Should().BeTrue();
       }

       [Fact]
       public async Task ReadThenWrite_RoundTripsThroughStreamingPassThrough()
       {
           using var srcFx = new GmailWireMockFixture();
           srcFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(srcFx.Fixture("labels.list.json")));
           srcFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(srcFx.Fixture("messages.list.json")));
           srcFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages/*").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(srcFx.Fixture("messages.get.raw.json")));

           await using var source = new GmailSourceProvider(srcFx.CreateService(), "me");

           CanonicalMessage? captured = null;
           await foreach (var m in source.ReadMessagesAsync(FolderPath.Parse("Work/Clients/Acme"), new(), CancellationToken.None))
           {
               captured = m;
               break;
           }
           captured.Should().NotBeNull();

           // OpenContentAsync must be replayable (retry-safe).
           byte[] first, second;
           await using (var s1 = await captured!.OpenContentAsync(CancellationToken.None))
           using (var ms1 = new MemoryStream()) { await s1.CopyToAsync(ms1); first = ms1.ToArray(); }
           await using (var s2 = await captured!.OpenContentAsync(CancellationToken.None))
           using (var ms2 = new MemoryStream()) { await s2.CopyToAsync(ms2); second = ms2.ToArray(); }
           first.Should().Equal(second);

           using var dstFx = new GmailWireMockFixture();
           dstFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(dstFx.Fixture("labels.list.json")));
           dstFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages/import").UsingPost())
             .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(dstFx.Fixture("messages.import.json")));

           await using var dest = new GmailDestinationProvider(dstFx.CreateService(), "me");
           var result = await dest.WriteMessageAsync(FolderPath.Parse("Work/Clients/Acme"), captured!, CancellationToken.None);

           result.Written.Should().BeTrue();

           var import = System.Array.Find(dstFx.Server.LogEntries.ToArray(),
               e => e.RequestMessage.Path == "/gmail/v1/users/me/messages/import");
           var body = import!.RequestMessage.Body ?? "";
           // The imported raw is base64url of the original RFC822; decode and assert Message-ID preserved.
           var rawField = System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("raw").GetString()!;
           var decoded = Encoding.UTF8.GetString(GmailRawCodec.DecodeBase64Url(rawField));
           decoded.Should().Contain("<acme-001@example.com>");
       }
   }
   ```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailContractConformanceTests` → expected FAIL initially if the source's `OpenContentAsync` is not replayable; this drives the implementation choice (Task 8 already captures bytes in a closure, so this should pass once Task 8/9 are in — if it fails, the failure pinpoints a single-use stream regression to fix).
3. - [ ] If step 2 fails on replayability, ensure `GmailSourceProvider.ToCanonical` captures the decoded `byte[]` and returns a **new** `MemoryStream(captured, writable:false)` per call (already specified in Task 8). No new production code is expected; this task is a conformance guard. If a regression is found, fix `GmailSourceProvider.cs` minimally so each `OpenContentAsync` call returns a fresh readable stream over the same bytes.
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailContractConformanceTests` → expected PASS.
5. - [ ] Commit:
   ```
   git add src/EMaigrator.Connectors.Gmail.Tests/GmailContractConformanceTests.cs
   git commit -m "test(gmail): contract conformance + streaming-passthrough round-trip

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
   ```

---

### Task 12: Document deferred paid-Workspace live testing risk

**Goal:** Record, in the connector docs, that the Gmail connector is validated only against recorded WireMock fixtures (paid Google Workspace live testing deferred per DESIGN.md §17) and exactly how to record fresh fixtures from a real tenant when one becomes available.

**Files:**
- Create: `docs/connectors/gmail-testing.md`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/DocumentationPresenceTests.cs`

**Acceptance Criteria:**
- [ ] `docs/connectors/gmail-testing.md` exists and contains: (a) a bold deferred-risk statement matching DESIGN.md §17 wording (Gmail connector validated only against recorded fixtures until a real Google migration runs); (b) the minimal scope justification (`https://mail.google.com/` only); (c) a step-by-step "how to record fresh fixtures" recipe (create SA + DWD, capture labels.list / messages.list / messages.get?format=raw / labels.create / messages.import / a 429).
- [ ] `DocumentationPresenceTests` asserts the doc file is present at the expected relative path and contains the marker phrases "recorded fixtures" and "https://mail.google.com/".

**Verify:** `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~DocumentationPresenceTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Gmail.Tests/DocumentationPresenceTests.cs`:
   ```csharp
   using System.IO;
   using FluentAssertions;
   using Xunit;

   namespace EMaigrator.Connectors.Gmail.Tests;

   public class DocumentationPresenceTests
   {
       private static string RepoRoot()
       {
           var dir = new DirectoryInfo(AppContext.BaseDirectory);
           while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EMaigrator.sln")))
               dir = dir.Parent;
           // Fallback: walk up to a folder that has a 'docs' directory.
           if (dir is null)
           {
               dir = new DirectoryInfo(AppContext.BaseDirectory);
               while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "docs")))
                   dir = dir.Parent;
           }
           dir.Should().NotBeNull();
           return dir!.FullName;
       }

       [Fact]
       public void GmailTestingDoc_ExistsAndDocumentsDeferredRiskAndScope()
       {
           var path = Path.Combine(RepoRoot(), "docs", "connectors", "gmail-testing.md");
           File.Exists(path).Should().BeTrue($"expected doc at {path}");

           var text = File.ReadAllText(path);
           text.Should().Contain("recorded fixtures");
           text.Should().Contain("https://mail.google.com/");
       }
   }
   ```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~DocumentationPresenceTests` → expected FAIL: the doc does not exist.
3. - [ ] Create `docs/connectors/gmail-testing.md`:
   ```markdown
   # Gmail Connector — Testing & Scope Notes

   ## Deferred live-testing risk (DESIGN.md §17)

   **Paid Google Workspace live testing is deferred.** Until a real Google migration runs end-to-end,
   the Gmail connector is validated **only against recorded fixtures** (WireMock.Net replaying captured
   Gmail v1 API responses). This is an accepted, documented risk: the recorded shapes may drift from
   live Gmail behavior (label visibility quirks, import edge cases, quota responses). The connector is
   not certified against a live tenant for v1.

   ## Auth & scope

   - Auth method: **BYO service account + domain-wide delegation (DWD)** — `AuthMethod.GmailServiceAccountDwd`.
   - Config: the impersonated mailbox is supplied via `ConnectionDescriptor.Settings["accountEmail"]`;
     the service-account JSON key is supplied via `SecretBundle.Values["serviceAccountJson"]`.
   - **OAuth scope is the single, minimal `https://mail.google.com/`.** No narrower scope authorizes
     both `messages.get?format=raw` (full-fidelity read) and `messages.import` (write with preserved
     internalDate); `gmail.readonly` cannot write, and `gmail.modify` cannot import arbitrary raw mail.
     The broad-but-justified single scope is the least privilege that satisfies a non-destructive copy.
   - The SA JSON is parsed in-memory only (`GoogleCredential.FromJson`), never written to disk, never
     logged, and held transiently for the lifetime of the provider.

   ## Recording fresh fixtures from a real tenant

   When a Google Workspace test tenant is available:

   1. In Google Cloud, create a service account; enable **domain-wide delegation**; in the Workspace
      Admin console authorize the SA client id for scope `https://mail.google.com/` only.
   2. Authenticate as the SA impersonating a seeded test mailbox.
   3. Capture each response body verbatim and save under `src/EMaigrator.Connectors.Gmail.Tests/Fixtures/`:
      - `GET users/me/labels` → `labels.list.json` (must include system + nested user labels)
      - `GET users/me/messages?labelIds=<id>` → `messages.list.json`
      - `GET users/me/messages/{id}?format=raw` → `messages.get.raw.json`
      - `POST users/me/labels` → `labels.create.json`
      - `POST users/me/messages/import?internalDateSource=dateHeader` → `messages.import.json`
      - A throttled call → `error.429.json` (reason `rateLimitExceeded`)
   4. Scrub any real addresses/ids to synthetic values before committing.
   5. Re-run `dotnet test src/EMaigrator.Connectors.Gmail.Tests` — all fixture-driven tests must stay green.
   ```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~DocumentationPresenceTests` → expected PASS.
5. - [ ] Commit:
   ```
   git add docs/connectors/gmail-testing.md src/EMaigrator.Connectors.Gmail.Tests/DocumentationPresenceTests.cs
   git commit -m "docs(gmail): document deferred live-testing risk and minimal scope

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
   ```

---

### Task 13: Security Verification (userGate)

**Goal:** Prove the Gmail connector's security focus: the service-account JSON key is never logged and held only transiently; DWD impersonation uses the minimal `https://mail.google.com/` scope only; the raw key is never written to disk; and quota/auth errors never leak the SA identity or impersonated mailbox to end users.

**USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in acceptanceCriteria has been re-validated independently, with output captured.

**Files:**
- Create: `src/EMaigrator.Connectors.Gmail.Tests/GmailSecurityVerificationTests.cs`
- Create: `src/EMaigrator.Connectors.Gmail.Tests/RecordingTextWriter.cs`

**Acceptance Criteria:**
- [ ] **Minimal scope, justified:** `GmailServiceFactory.RequiredScopes` equals exactly `["https://mail.google.com/"]` (no `https://www.googleapis.com/auth/gmail.*` broad family, no Drive/Calendar scopes) — asserted by test.
- [ ] **Key never on disk:** building a service via `GmailServiceFactory.Create(config)` creates **zero** new files under `Path.GetTempPath()` and the current directory (before/after file-count diff is 0); asserted by test.
- [ ] **Key not exposed publicly:** reflection over `GmailConnectionConfig`'s public members finds **no** member returning the SA JSON / private-key PEM; asserted by test (grep-equivalent over public string properties for the substring `PRIVATE KEY`).
- [ ] **No SA identity / mailbox in error surface:** for a 403 `quotaExceeded` and a 401 `authError` whose raw Google message embeds `victim@example.com` and the SA email `emaigrator-test@test-project.iam.gserviceaccount.com`, the normalized signature returned to callers contains neither `@`, nor `victim`, nor `gserviceaccount` — asserted by test, with the produced signatures captured/printed.
- [ ] **No credential in any log/exception text emitted by the providers:** a `RecordingTextWriter` installed as `Console.Out`/`Console.Error` during a forced `TestConnectionAsync` failure (401 fixture carrying the SA email + mailbox) captures **zero** occurrences of the SA email, the impersonated mailbox, or the literal `PRIVATE KEY`; asserted on the captured buffer.
- [ ] **TLS enforced / no arbitrary-host exfiltration:** the production `GmailService` built by `GmailServiceFactory.Create(config)` has a `BaseUri` whose scheme is `https` and whose host is `gmail.googleapis.com` (the factory pins the Google endpoint; a caller-supplied `accountEmail`/SA JSON can never redirect Gmail traffic to an attacker host or downgrade to cleartext) — asserted by test.
- [ ] **DWD justification documented:** `docs/connectors/gmail-testing.md` contains the scope justification (re-checked here) so the broad-but-minimal scope is defensible.

**Verify:** `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailSecurityVerificationTests` → all pass (capture and retain console output of the test run as evidence).

**Steps:**
1. - [ ] Write the failing helper `src/EMaigrator.Connectors.Gmail.Tests/RecordingTextWriter.cs`:
   ```csharp
   using System.IO;
   using System.Text;

   namespace EMaigrator.Connectors.Gmail.Tests;

   /// <summary>Captures everything written so tests can assert no credential ever appears in output.</summary>
   public sealed class RecordingTextWriter : TextWriter
   {
       private readonly StringBuilder _sb = new();
       public override Encoding Encoding => Encoding.UTF8;
       public override void Write(char value) => _sb.Append(value);
       public override void Write(string? value) => _sb.Append(value);
       public string Captured => _sb.ToString();
   }
   ```
   Write the failing test `src/EMaigrator.Connectors.Gmail.Tests/GmailSecurityVerificationTests.cs`:
   ```csharp
   using System;
   using System.Collections.Generic;
   using System.IO;
   using System.Linq;
   using System.Threading;
   using System.Threading.Tasks;
   using EMaigrator.Connectors.Gmail;
   using EMaigrator.Core.Abstractions;
   using EMaigrator.Core.Model;
   using FluentAssertions;
   using Google;
   using Google.Apis.Requests;
   using WireMock.RequestBuilders;
   using WireMock.ResponseBuilders;
   using Xunit;

   namespace EMaigrator.Connectors.Gmail.Tests;

   public class GmailSecurityVerificationTests
   {
       private const string SaEmail = "emaigrator-test@test-project.iam.gserviceaccount.com";
       private const string Mailbox = "victim@example.com";

       [Fact]
       public void Scope_IsMinimalMailGoogleComOnly()
       {
           GmailServiceFactory.RequiredScopes.Should().Equal(new[] { "https://mail.google.com/" });
           GmailServiceFactory.RequiredScopes.Should().NotContain(s => s.Contains("drive"));
           GmailServiceFactory.RequiredScopes.Should().NotContain(s => s.Contains("calendar"));
           GmailServiceFactory.RequiredScopes.Should().NotContain(s => s.Contains("gmail.readonly"));
           GmailServiceFactory.RequiredScopes.Should().NotContain(s => s.Contains("gmail.modify"));
       }

       [Fact]
       public void ServiceConstruction_WritesNoKeyToDisk()
       {
           var secrets = new SecretBundle(new Dictionary<string, string> { ["serviceAccountJson"] = TestServiceAccount.Json });
           var descriptor = new ConnectionDescriptor
           {
               Provider = new ProviderId("gmail"),
               Auth = AuthMethod.GmailServiceAccountDwd,
               Settings = new Dictionary<string, string> { ["accountEmail"] = Mailbox },
           };
           var config = GmailConnectionConfig.FromDescriptor(descriptor, secrets);

           var tempBefore = Directory.GetFiles(Path.GetTempPath()).Length;
           var cwdBefore = Directory.GetFiles(Directory.GetCurrentDirectory()).Length;

           using var service = GmailServiceFactory.Create(config);

           Directory.GetFiles(Path.GetTempPath()).Length.Should().Be(tempBefore);
           Directory.GetFiles(Directory.GetCurrentDirectory()).Length.Should().Be(cwdBefore);
       }

       [Fact]
       public void ProductionService_PinsHttpsGoogleEndpoint()
       {
           var secrets = new SecretBundle(new Dictionary<string, string> { ["serviceAccountJson"] = TestServiceAccount.Json });
           var descriptor = new ConnectionDescriptor
           {
               Provider = new ProviderId("gmail"),
               Auth = AuthMethod.GmailServiceAccountDwd,
               Settings = new Dictionary<string, string> { ["accountEmail"] = Mailbox },
           };
           var config = GmailConnectionConfig.FromDescriptor(descriptor, secrets);

           using var service = GmailServiceFactory.Create(config);

           var baseUri = new Uri(service.BaseUri);
           baseUri.Scheme.Should().Be("https");          // TLS enforced; no cleartext downgrade
           baseUri.Host.Should().Be("gmail.googleapis.com"); // endpoint pinned; not caller-controllable
       }

       [Fact]
       public void Config_NeverExposesPrivateKeyViaPublicMembers()
       {
           var secrets = new SecretBundle(new Dictionary<string, string> { ["serviceAccountJson"] = TestServiceAccount.Json });
           var descriptor = new ConnectionDescriptor
           {
               Provider = new ProviderId("gmail"),
               Auth = AuthMethod.GmailServiceAccountDwd,
               Settings = new Dictionary<string, string> { ["accountEmail"] = Mailbox },
           };
           var config = GmailConnectionConfig.FromDescriptor(descriptor, secrets);

           var exposed = config.GetType().GetProperties()
               .Where(p => p.PropertyType == typeof(string) && p.GetGetMethod()?.IsPublic == true)
               .Select(p => (string?)p.GetValue(config))
               .Any(v => v != null && (v.Contains("PRIVATE KEY") || v.Contains("\"private_key\"")));

           exposed.Should().BeFalse();
       }

       [Theory]
       [InlineData(System.Net.HttpStatusCode.Forbidden, "quotaExceeded", "gmail:403:quotaExceeded")]
       [InlineData(System.Net.HttpStatusCode.Unauthorized, "authError", "gmail:401:authError")]
       public void ErrorSignature_LeaksNeitherMailboxNorServiceAccount(
           System.Net.HttpStatusCode status, string reason, string expected)
       {
           var msg = $"User rate limit exceeded for {Mailbox}; service account {SaEmail}";
           var err = new RequestError
           {
               Code = (int)status,
               Message = msg,
               Errors = new List<SingleError> { new SingleError { Reason = reason, Message = msg } },
           };
           var ex = new GoogleApiException("gmail", msg) { HttpStatusCode = status, Error = err };

           var sig = GmailErrorNormalizer.Normalize(ex);

           sig.Should().Be(expected);
           sig.Should().NotContain("@");
           sig.Should().NotContain("victim");
           sig.Should().NotContain("gserviceaccount");
       }

       [Fact]
       public async Task TestConnectionFailure_EmitsNoCredentialToConsole()
       {
           using var fx = new GmailWireMockFixture();
           fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(401)
                 .WithHeader("Content-Type", "application/json")
                 .WithBody($"{{\"error\":{{\"code\":401,\"message\":\"Invalid Credentials for {Mailbox} via {SaEmail}\",\"errors\":[{{\"reason\":\"authError\"}}]}}}}"));

           var origOut = Console.Out;
           var origErr = Console.Error;
           var rec = new RecordingTextWriter();
           Console.SetOut(rec);
           Console.SetError(rec);
           try
           {
               await using var src = new GmailSourceProvider(fx.CreateService(), "me");
               var result = await src.TestConnectionAsync(CancellationToken.None);
               result.Ok.Should().BeFalse();
               result.ErrorCode.Should().Be("gmail:401:authError");
               (result.RawDetail ?? "").Should().NotContain("@");
           }
           finally
           {
               Console.SetOut(origOut);
               Console.SetError(origErr);
           }

           rec.Captured.Should().NotContain(SaEmail);
           rec.Captured.Should().NotContain(Mailbox);
           rec.Captured.Should().NotContain("PRIVATE KEY");
       }

       [Fact]
       public void Docs_RecordScopeJustification()
       {
           var dir = new DirectoryInfo(AppContext.BaseDirectory);
           while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "docs")))
               dir = dir.Parent;
           dir.Should().NotBeNull();
           var doc = File.ReadAllText(Path.Combine(dir!.FullName, "docs", "connectors", "gmail-testing.md"));
           doc.Should().Contain("https://mail.google.com/");
           doc.Should().Contain("least privilege");
       }
   }
   ```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailSecurityVerificationTests` → expected FAIL: helper/test compile errors and the docs-justification assertion will fail until the doc contains the phrase "least privilege".
3. - [ ] Make minimal changes to satisfy the gate observables: (a) ensure `docs/connectors/gmail-testing.md` contains the exact phrase "least privilege" in the scope justification (edit the scope bullet to read "...the least privilege that satisfies a non-destructive copy."); (b) confirm `GmailConnectionConfig.ServiceAccountJson` is `internal` (not public) so the reflection check passes; (c) confirm `GmailErrorNormalizer.Normalize` already strips to the closed-vocabulary reason (Task 5) — no production change expected. No new credential-handling code should be needed; if any assertion fails, fix the offending production path minimally (e.g., downgrade an accidentally-public property to internal, or remove an interpolated mailbox from a diagnostic string).
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailSecurityVerificationTests` → expected PASS. Capture the full console output of this run as the gate evidence (each assertion independently re-validates a named observable: scope set, temp-file diff, reflection over public members, normalized signatures, console capture buffer).
5. - [ ] Commit:
   ```
   git add src/EMaigrator.Connectors.Gmail.Tests/GmailSecurityVerificationTests.cs src/EMaigrator.Connectors.Gmail.Tests/RecordingTextWriter.cs docs/connectors/gmail-testing.md
   git commit -m "test(gmail): security verification — minimal scope, no key on disk, no identity leak

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
   ```

---

### Task 14: Functional Verification — full Gmail→Gmail fixture migration

**Goal:** Prove the connector's headline behavior end-to-end against recorded fixtures: discover folders, read a raw message from a Gmail source, and write it into a Gmail destination (ensuring the label, importing with preserved date and read state, and confirming dedup) — the complete source→canonical→destination path the engine relies on.

**Files:**
- Create: `src/EMaigrator.Connectors.Gmail.Tests/GmailFunctionalVerificationTests.cs`

**Acceptance Criteria:**
- [ ] An end-to-end test: source `ListFoldersAsync` returns the mappable folders; for `Work/Clients/Acme`, `ReadMessagesAsync` yields the fixture message; the destination `EnsureFolderAsync` creates the target label (POST observed) when absent; `WriteMessageAsync` returns `Written == true` with the import id; the import request carries `internalDateSource=dateHeader` and (since the message is UNREAD) `UNREAD` in its labels.
- [ ] After the write, `ExistsByMessageIdAsync` (stubbed to the message-list fixture) returns `true` for the original `Message-ID`, proving the dedup path works.
- [ ] The whole flow performs **zero** real network calls (only WireMock) and never writes message content to disk (the only stream materializations are in-memory `MemoryStream`s).
- [ ] The full connector test suite passes (`dotnet test src/EMaigrator.Connectors.Gmail.Tests`).

**Verify:** `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailFunctionalVerificationTests` → all pass; then `dotnet test src/EMaigrator.Connectors.Gmail.Tests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Gmail.Tests/GmailFunctionalVerificationTests.cs`:
   ```csharp
   using System;
   using System.Linq;
   using System.Threading;
   using System.Threading.Tasks;
   using EMaigrator.Connectors.Gmail;
   using EMaigrator.Core.Model;
   using FluentAssertions;
   using WireMock.RequestBuilders;
   using WireMock.ResponseBuilders;
   using Xunit;

   namespace EMaigrator.Connectors.Gmail.Tests;

   public class GmailFunctionalVerificationTests
   {
       [Fact]
       public async Task EndToEnd_DiscoverReadEnsureImportDedup()
       {
           // --- SOURCE ---
           using var srcFx = new GmailWireMockFixture();
           srcFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(srcFx.Fixture("labels.list.json")));
           srcFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(srcFx.Fixture("messages.list.json")));
           srcFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages/*").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(srcFx.Fixture("messages.get.raw.json")));

           await using var source = new GmailSourceProvider(srcFx.CreateService(), "me");

           var folders = await source.ListFoldersAsync(CancellationToken.None);
           folders.Select(f => f.Path.ToString()).Should().Contain("Work/Clients/Acme");

           CanonicalMessage? msg = null;
           await foreach (var m in source.ReadMessagesAsync(FolderPath.Parse("Work/Clients/Acme"), new(), CancellationToken.None))
           { msg = m; break; }
           msg.Should().NotBeNull();
           msg!.Flags.Should().NotHaveFlag(MessageFlags.Seen); // UNREAD in fixture

           // --- DESTINATION (label absent => must be created) ---
           using var dstFx = new GmailWireMockFixture();
           // labels.list returns a set WITHOUT the target "Migrated/Acme" label.
           dstFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(dstFx.Fixture("labels.list.json")));
           dstFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingPost())
             .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(dstFx.Fixture("labels.create.json")));
           dstFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages/import").UsingPost())
             .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(dstFx.Fixture("messages.import.json")));
           dstFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(dstFx.Fixture("messages.list.json")));

           await using var dest = new GmailDestinationProvider(dstFx.CreateService(), "me");

           await dest.EnsureFolderAsync(FolderPath.Parse("Migrated/Acme"), CancellationToken.None);
           dstFx.Server.LogEntries.Should().Contain(e =>
               e.RequestMessage.Method == "POST" && e.RequestMessage.Path == "/gmail/v1/users/me/labels");

           var write = await dest.WriteMessageAsync(FolderPath.Parse("Migrated/Acme"), msg!, CancellationToken.None);
           write.Written.Should().BeTrue();
           write.DestMessageId.Should().Be("18f0bb33dd44ee01");

           var import = System.Array.Find(dstFx.Server.LogEntries.ToArray(),
               e => e.RequestMessage.Path == "/gmail/v1/users/me/messages/import");
           import!.RequestMessage.RawQuery.Should().Contain("internalDateSource=dateHeader");
           (import.RequestMessage.Body ?? "").Should().Contain("UNREAD");

           // --- DEDUP ---
           var exists = await dest.ExistsByMessageIdAsync(
               FolderPath.Parse("Migrated/Acme"), msg!.MessageId!, CancellationToken.None);
           exists.Should().BeTrue();
       }
   }
   ```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailFunctionalVerificationTests` → expected FAIL initially only if any earlier wiring is incomplete; otherwise it exercises the already-implemented providers. If it fails, the failure names the exact broken step (folder discovery, read, ensure-label, import query, or dedup) to fix minimally in the relevant provider file.
3. - [ ] If step 2 reveals a gap (e.g., `EnsureFolderAsync` issuing a create when the label exists, or the import query missing `internalDateSource`), fix the minimal production code in `GmailDestinationProvider.cs` / `GmailSourceProvider.cs` to satisfy the failing assertion — no new files.
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Gmail.Tests --filter FullyQualifiedName~GmailFunctionalVerificationTests` then `dotnet test src/EMaigrator.Connectors.Gmail.Tests` → expected PASS (whole suite green).
5. - [ ] Commit:
   ```
   git add src/EMaigrator.Connectors.Gmail.Tests/GmailFunctionalVerificationTests.cs
   git commit -m "test(gmail): functional verification — end-to-end fixture migration

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
   ```
