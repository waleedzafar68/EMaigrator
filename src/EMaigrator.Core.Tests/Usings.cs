// Xunit is already a global using via the test csproj (<Using Include="Xunit" />),
// so it must NOT be repeated here (duplicate global using = CS0105 = error under
// TreatWarningsAsErrors). The plan's test files rely on FluentAssertions globally.
global using FluentAssertions;
