﻿namespace MonoDevelop.FSharp.Formatting

open System
open System.Diagnostics
open MonoDevelop.Ide.Gui.Content
open MonoDevelop.Ide
open MonoDevelop.Projects.Policies
open MonoDevelop.Ide.CodeFormatting
open Fantomas
open Fantomas.FormatConfig
open Microsoft.FSharp.Compiler

#if DEBUG
type Debug = Console
#endif

type FormattingOption =
    | Document
    | Selection of int * int

type FSharpFormatter() = 
    let offsetToPos (positions : _ []) offset =
        let rec searchPos start finish = 
            if start >= finish then None
            elif start + 1 = finish then
                Some (Range.mkPos (start + 1) (offset - positions.[start]))  
            else
                let mid = (start + finish) / 2
                if offset = positions.[mid] then Some (Range.mkPos (mid + 1) 0)
                elif offset > positions.[mid] then searchPos mid finish
                else searchPos start mid
        
        searchPos 0 (positions.Length - 1)

    let format isFsiFile (textStylePolicy : TextStylePolicy) (formattingPolicy : FSharpFormattingPolicy) input formattingOption =
        let config = FormatConfig.Default
        match formattingOption with
        | Document -> 
            try 
                CodeFormatter.formatSourceString isFsiFile input config
            with :? FormatException as ex ->
                Debug.WriteLine("Error occurs: {0}", ex.Message)
                input

        | Selection(fromOffset, toOffset) ->
            // Convert from offsets to line and column position
            let positions = 
                input.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
                |> Seq.map (fun s -> String.length s + 1)
                |> Seq.scan (+) 0
                |> Seq.toArray
            let fromOffset = max 0 fromOffset
            let toOffset = min input.Length toOffset
            Debug.WriteLine("**Fantomas**: Offsets from {0} to {1}", fromOffset, toOffset)
            let startPos = offsetToPos positions fromOffset
            let endPos = offsetToPos positions toOffset
            Diagnostics.Debug.Assert(startPos.IsSome && endPos.IsSome, "Offsets are within valid ranges.")
            let r = Range.mkRange "/tmp.fsx" startPos.Value endPos.Value
            Debug.WriteLine("**Fantomas**: Try to format range {0}.", r)
            // This is not going to be sufficient if Fantomas expands the range for valid F# code.
            // It should be better to get handle of the document and replace the whole text
            let output =
                try 
                    CodeFormatter.formatSourceString isFsiFile input config
                with :? FormatException as ex ->
                    Debug.WriteLine("Error occurs: {0}", ex.Message)
                    input
            let delta = input.Length - toOffset
            output.[fromOffset..output.Length - delta - 1]

    let formatText (policyParent : PolicyContainer) (mimeTypeInheritanceChain : string seq) (input : string) formattingOption =
        let isFsiFile = 
            if Seq.isEmpty mimeTypeInheritanceChain then false
            else 
                let ext = Seq.head mimeTypeInheritanceChain
                ext.ToLower() <> "text/x-fsharp"
        Debug.WriteLine("**Fantomas**: Is this an fsi file? {0}", isFsiFile)
        let textStylePolicy = policyParent.Get<TextStylePolicy>(mimeTypeInheritanceChain)
        let formattingPolicy = policyParent.Get<FSharpFormattingPolicy>(mimeTypeInheritanceChain)

        format isFsiFile textStylePolicy formattingPolicy input formattingOption

    // We don't support on-the-fly formatting and smart indenting yet. 
    // There's no point to use IAdvanceCodeFormatter.
    interface ICodeFormatter with
        member __.FormatText(policyParent, mimeTypeInheritanceChain, input) =
            Debug.WriteLine("**Fantomas**: Formatting document")
            formatText policyParent mimeTypeInheritanceChain input Document

        member __.FormatText(policyParent, mimeTypeInheritanceChain, input, fromOffset, toOffset) =
            Debug.WriteLine("**Fantomas**: Formatting selection")
            formatText policyParent mimeTypeInheritanceChain input (Selection(fromOffset, toOffset))

