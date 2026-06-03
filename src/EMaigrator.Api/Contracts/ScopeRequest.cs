using System;
using System.Collections.Generic;

namespace EMaigrator.Api.Contracts;

/// <summary>One source→dest mailbox pair as posted in a JSON scope request body.</summary>
public sealed record ScopePairDto(string SourceMailbox, string DestMailbox);

/// <summary>
/// The JSON body of <c>PUT /migrations/{id}/scope</c>, mirroring the engine's scope spec: whether the job
/// is a batch, its explicit mailbox pairs, and the optional folder/date filters. (A multipart CSV upload
/// is the alternate path; see <see cref="Services.CsvMailboxParser"/>.)
/// </summary>
public sealed record ScopeRequest(
    bool IsBatch,
    IReadOnlyList<ScopePairDto>? Pairs,
    IReadOnlyList<string>? IncludeFolders,
    IReadOnlyList<string>? ExcludeFolders,
    DateTimeOffset? Since,
    DateTimeOffset? Before);
