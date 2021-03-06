namespace Tools.Linting

[<RequireQualifiedAccess>]
module FSharpLinter =
    open FSharp.Compiler.Range
    open FSharpLint.Application
    open FSharpLint.Framework.Configuration
    open FSharpLint.Framework
    open System

    let private writeLine (str: string) (color: ConsoleColor) (writer: IO.TextWriter) =
        let originalColour = Console.ForegroundColor
        Console.ForegroundColor <- color
        writer.WriteLine str
        Console.ForegroundColor <- originalColour

    let private writeInfoLine (str: string) = writeLine str ConsoleColor.White Console.Out
    let private writeWarningLine (str: string) = writeLine str ConsoleColor.Yellow Console.Out
    let private writeErrorLine (str: string) = writeLine str ConsoleColor.Red Console.Error

    let private parserProgress =
        function
        | Starting(file) -> String.Format(Resources.GetString("ConsoleStartingFile"), file) |> writeInfoLine
        | ReachedEnd(_, warnings) ->
            String.Format(Resources.GetString("ConsoleFinishedFile"), List.length warnings) |> writeInfoLine
        | Failed(file, parseException) ->
            String.Format(Resources.GetString("ConsoleFailedToParseFile"), file) |> writeErrorLine
            "Exception Message:" + Environment.NewLine + parseException.Message + Environment.NewLine
            + "Exception Stack Trace:" + Environment.NewLine + parseException.StackTrace + Environment.NewLine
            |> writeErrorLine

    let mutable private collectWarning = List.empty<string>

    let private getWarnings() =
        match collectWarning.Length = 0 with
        | true -> None
        | false ->
            let warns = collectWarning
            collectWarning <- List.empty<string>
            Some(warns)

    let getErrorMessage (range:FSharp.Compiler.Range.range) =
        let error = Resources.GetString("LintSourceError")

        String.Format(error, range.StartLine, range.StartColumn)

    let highlightErrorText (range:range) (errorLine:string) =
        let highlightColumnLine =
            if String.length errorLine = 0 then "^"
            else
                errorLine
                |> Seq.mapi (fun i _ -> if i = range.StartColumn then "^" else " ")
                |> Seq.reduce (+)

        errorLine + Environment.NewLine + highlightColumnLine

    let private writeLintWarning (warning : Suggestion.LintWarning) =
        let highlightedErrorText = highlightErrorText warning.Details.Range (getErrorMessage warning.Details.Range)
        let warnMsg = warning.Details.Message + Environment.NewLine + highlightedErrorText + Environment.NewLine + warning.ErrorText

        collectWarning <- ((if collectWarning.Length > 0 then Environment.NewLine + warnMsg
                            else warnMsg)
                           :: collectWarning)

        warnMsg |> writeWarningLine
        String.replicate 80 "*" |> writeInfoLine

    let private handleError (str: string) = writeErrorLine str

    let private handleLintResult =
        function
        | LintResult.Failure(failure) -> handleError failure.Description
        | _ -> ()

    let private parseInfo (webFile: bool) =
        let config =
            if webFile then
                { Configuration.formatting = None
                  Configuration.conventions =
                    { ConventionsConfig.recursiveAsyncFunction = None
                      ConventionsConfig.redundantNewKeyword = None
                      ConventionsConfig.nestedStatements = None
                      ConventionsConfig.reimplementsFunction = None
                      ConventionsConfig.canBeReplacedWithComposition = None
                      ConventionsConfig.raiseWithTooManyArgs = None
                      ConventionsConfig.sourceLength = None
                      ConventionsConfig.naming = 
                        { NamesConfig.interfaceNames = Some { RuleConfig.enabled = false; config = None }
                          NamesConfig.exceptionNames = None
                          NamesConfig.typeNames = None
                          NamesConfig.recordFieldNames = None
                          NamesConfig.enumCasesNames = None
                          NamesConfig.unionCasesNames = None
                          NamesConfig.moduleNames = None
                          NamesConfig.literalNames = None
                          NamesConfig.namespaceNames = None
                          NamesConfig.memberNames = Some { RuleConfig.enabled = false; config = None }
                          NamesConfig.parameterNames = None
                          NamesConfig.measureTypeNames = None
                          NamesConfig.activePatternNames = None
                          NamesConfig.publicValuesNames = None
                          NamesConfig.nonPublicValuesNames = None }
                        |> Some
                      ConventionsConfig.binding = None
                      ConventionsConfig.numberOfItems = None }
                    |> Some
                  Configuration.typography = None
                  Configuration.ignoreFiles = None
                  Configuration.hints = None }
                |> ConfigurationParam.Configuration
            else ConfigurationParam.Default
        { CancellationToken = None
          ReceivedWarning = Some writeLintWarning
          Configuration = config
          ReportLinterProgress = Some parserProgress
          ReleaseConfiguration = None }

    let lintFiles (fileList: (bool * string list) list) =
        let lintFile (webFile: bool) (file: string) =
            let sw = Diagnostics.Stopwatch.StartNew()
            try
                Lint.lintFile (parseInfo webFile) file |> handleLintResult
                sw.Stop()
            with e ->
                sw.Stop()
                let error =
                    "Lint failed while analysing " + file + "." + Environment.NewLine + "Failed with: " + e.Message
                    + Environment.NewLine + "Stack trace:" + e.StackTrace
                error |> handleError

        fileList
        |> List.iter (fun (webFile, fList) -> fList |> List.iter (lintFile webFile))
