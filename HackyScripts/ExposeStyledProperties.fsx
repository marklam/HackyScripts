#!/usr/bin/env -S dotnet fsi

#r "nuget: Mono.Cecil"

// This is a workaround for EXPIRY https://github.com/AvaloniaUI/Avalonia/issues/17064
// Avalonia expects control properties to be defined in a static field, and F# makes those internal by default.
// As a result, the Avalonia bindings fail to call them (if they even compile, since reference assemblies don't contain internal fields).

// If this script is in the folder above the project, add this to the fsproj file:

//  <Target Name="PostBuild" AfterTargets="PostBuildEvent">
//    <Exec Command="dotnet fsi $(MSBuildThisFileDirectory)..\ExposeStyledProperties.fsx $(TargetPath) $(TargetRefPath)" />
//  </Target>

open System
open System.IO
open Mono.Cecil
open Mono.Cecil.Cil

let makeInternalFieldsPublic (assemblyPath: string) (pdbPath : string) (referenceAssemblyPath : string) =
    let symbolStream = File.Open(pdbPath, FileMode.Open)
    let symbolProvider = PortablePdbReaderProvider()
    let assemblyReaderParameters =
        ReaderParameters(
            SymbolReaderProvider = symbolProvider,
            ReadSymbols = true,
            SymbolStream = symbolStream,
            ReadWrite = true)

    let assembly = AssemblyDefinition.ReadAssembly(assemblyPath, assemblyReaderParameters)
    symbolStream.Close()

    let referenceAssembly = AssemblyDefinition.ReadAssembly(referenceAssemblyPath, ReaderParameters(ReadWrite = true))
    for typeDef in assembly.MainModule.Types do
        for field in typeDef.Fields do
            if  field.IsAssembly &&
                field.Name.EndsWith "Property" &&
                field.FieldType.Name = "StyledProperty`1" then
                    field.IsPublic <- true
                    field.IsInitOnly <- true

                    match referenceAssembly.MainModule.Types |> Seq.tryFind (fun t -> t.FullName = typeDef.FullName) with
                    | None ->
                        eprintfn $"Patched type %s{typeDef.FullName} not found in reference assembly"
                    | Some refTypeDef ->
                        match refTypeDef.Fields |> Seq.tryFind (fun f -> f.Name = field.Name) with
                        | Some _ ->
                            eprintfn $"Patched field %s{typeDef.FullName}.%s{field.Name} found unexpectedly in reference assembly"
                        | None ->
                            let newFieldType = referenceAssembly.MainModule.ImportReference(field.FieldType)
                            let newField = new FieldDefinition(field.Name, field.Attributes, newFieldType)
                            refTypeDef.Fields.Add(newField)

                    printfn $"Patched field %s{typeDef.FullName}.%s{field.Name}"

    referenceAssembly.Write()

    let symbolStream = File.Create(pdbPath)
    let symbolProvider = PortablePdbWriterProvider()
    let assemblyWriterParameters =
            WriterParameters(
                    SymbolStream = symbolStream,
                    WriteSymbols = true,
                    SymbolWriterProvider = symbolProvider
                )

    assembly.Write(assemblyWriterParameters)
    symbolStream.Close()

let args = Environment.GetCommandLineArgs()
let dlls = args |> Array.filter (fun path -> path.EndsWith ".dll")
let dll = dlls.[dlls.Length - 2]
let refdll = dlls.[dlls.Length - 1]
let pdb = Path.ChangeExtension(dll, ".pdb")
printfn "Assembly at %s, reference assembly at %s" dll refdll

makeInternalFieldsPublic dll pdb refdll