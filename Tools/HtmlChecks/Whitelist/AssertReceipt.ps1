function Assert-HtmlWhitelistReceipt($Report, [string]$Target) {
    $reflectionErrors = @($Report.UnusedReflectionControlDiagnostics | Where-Object { $_.Origin -eq 'Analyzer' -and $_.Severity -eq 'Error' })
    $pragmaErrors = @($Report.PragmaControlDiagnostics | Where-Object { $_.Origin -eq 'Analyzer' -and $_.Severity -eq 'Error' })
    $registrars = @($Report.DefaultRegistrationRoutines) -join ','
    $expectedNumeric = if ($Target -eq 'Ingame') { 2 } else { 1 }
    if ($Target -notin @('ModApi','Ingame') -or -not $Report.Success -or -not $Report.SetupSucceeded -or -not $Report.ActualWhitelistControlsPassed -or -not $Report.CandidateValidated -or -not $Report.SourceStableDuringGate -or $Report.CandidateSourceCount -lt 1 -or $Report.SelectedTargetNumeric -ne $expectedNumeric -or $Report.SelectedTarget -ne $Target -or $Report.AnalyzerType -ne 'VRage.Scripting.Analyzers.WhitelistDiagnosticAnalyzer' -or $Report.WhitelistOwner -ne 'VRage.Scripting.MyScriptWhitelist' -or $Report.ParseLanguageVersion -ne 'CSharp6' -or $Report.HostFramework -notmatch '^mscorlib, Version=4\.0\.0\.0,' -or $registrars -ne 'AllowDefaultNamespaces,AllowSandboxNamespaces,AllowSpaceEngineersNamespaces' -or @($Report.PositiveControlDiagnostics).Count -ne 0 -or $reflectionErrors.Count -lt 1 -or $pragmaErrors.Count -lt 1 -or $Report.CustomWhitelistEntriesAdded -or $Report.InjectedSyntaxAnnotations -or $Report.CandidateCodeExecuted -or $Report.NativeOrGameContextInitialized -or @($Report.CandidateDiagnostics).Count -ne 0) {
        throw "Whitelist receipt does not certify the actual unchanged $Target candidate and required controls."
    }
    if ($Target -eq 'Ingame') {
        if ($Report.SourcePreprocessing -ne 'CallerProvidedWrappedPB') { throw 'Ingame proof must analyze the authentic complete PB wrappers.' }
        $modErrors = @($Report.ModOnlyControlDiagnostics | Where-Object { $_.Origin -eq 'Analyzer' -and $_.Severity -eq 'Error' })
        $blacklistErrors = @($Report.DefaultBlacklistControlDiagnostics | Where-Object { $_.Origin -eq 'Analyzer' -and $_.Severity -eq 'Error' })
        $restrictions = @($Report.DefaultIngameBlacklistRestrictions | ForEach-Object { $_.Type + ':' + (@($_.Members) -join ',') }) -join ';'
        $expectedRestrictions = 'System.IO.Path:GetTempFileName;System.Globalization.CultureInfo:DefaultThreadCurrentCulture,DefaultThreadCurrentUICulture;System.Text.Encoding:RegisterProvider;System.Text.RegularExpressions.Regex:CacheSize'
        $rewriter = 'VRage.Scripting.Rewriters.TypeSafetyAndBlockRewriter'
        if (-not $Report.DefaultIngameBlacklistApplied -or $restrictions -ne $expectedRestrictions -or $Report.BlacklistEvidenceMethod -ne 'Sandbox.MySandboxGame.InitIlChecker' -or -not $Report.BlacklistEvidenceAssembly -or $modErrors.Count -lt 1 -or $blacklistErrors.Count -lt 1 -or -not $Report.OriginalSourceWhitelistCheckedBeforeRewrite -or $Report.CustomInjectedSyntaxAnnotations -or -not $Report.OfficialEngineRewriterMayProduceAnnotations -or $Report.RewriterType -ne $rewriter -or -not $Report.MemorySafeRewriteApplied -or -not $Report.RewrittenEmitSucceeded -or @($Report.PbCompilations).Count -ne $Report.CandidateSourceCount) {
            throw 'Ingame receipt does not certify stock restrictions, authentic target controls, and the official PB rewriter.'
        }
        foreach ($compilation in @($Report.PositiveControlMemorySafeRewrite) + @($Report.PbCompilations)) {
            if (-not $compilation.MemorySafeRewriteApplied -or $compilation.RewriterType -ne $rewriter -or -not $compilation.RewrittenTextChanged -or -not $compilation.RewrittenEmitSucceeded -or $compilation.EmittedBytes -lt 1 -or @($compilation.RewrittenDiagnostics).Count -ne 0 -or @($compilation.Sources).Count -lt 1) { throw 'A PB control or candidate did not change under the official memory-safe rewrite and emit successfully.' }
            foreach ($source in $compilation.Sources) { if ($source.RewrittenSourceSHA256 -notmatch '^[0-9A-F]{64}$') { throw 'PB rewrite evidence has no valid source fingerprint.' } }
        }
    }
    else {
        $ambiguity = @($Report.CompatibilityAmbiguityControlDiagnostics | Where-Object { $_.Origin -eq 'Compiler' -and $_.Id -eq 'CS0104' -and $_.Severity -eq 'Error' })
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { $headerHash = [BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes([string]$Report.ModCompatibilityHeader))).Replace('-','') }
        finally { $algorithm.Dispose() }
        if ($Report.SourcePreprocessing -ne 'InstalledOfficialModCompatibilityHeader' -or -not $Report.ModCompatibilityHeaderApplied -or -not $Report.ModCompatibilityControlsPassed -or $Report.ModCompatibilityHistoricalStringReplacementsApplied -or $Report.ModCompatibilityHeader -notmatch 'using Sandbox\.ModAPI;' -or $Report.ModCompatibilityHeader -notmatch '#line 1' -or $Report.ModCompatibilityHeaderSHA256 -ne $headerHash -or $Report.ModCompatibilityMethodILSHA256 -notmatch '^[0-9A-F]{64}$' -or -not $Report.ModCompatibilityEvidenceAssembly -or $Report.ModCompatibilityEvidenceMethod -ne 'Sandbox.Game.World.MyScriptManager.UpdateCompatibility' -or $ambiguity.Count -lt 1 -or @($Report.CompatibilityAliasPositiveDiagnostics).Count -ne 0 -or @($Report.PreprocessedSources).Count -ne $Report.CandidateSourceCount) { throw 'ModApi proof does not certify the installed official compatibility header and its ambiguity/alias controls.' }
        foreach ($source in $Report.PreprocessedSources) { if ($source.SHA256 -notmatch '^[0-9A-F]{64}$') { throw 'ModApi preprocessing evidence has no valid source fingerprint.' } }
    }
}
