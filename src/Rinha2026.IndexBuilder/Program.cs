using Rinha2026.IndexBuilder.Build;

IndexBuildOptions? options = IndexBuildOptionsParser.TryParse(args, out string? errorMessage);

if (options is null) {
	Console.Error.WriteLine(errorMessage);
	return 1;
}

await ReferenceCorpusBuilder.BuildAsync(options, CancellationToken.None);
return 0;
