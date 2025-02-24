using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DafnyCore;

namespace Microsoft.Dafny;


public static class CreateValidatorsCommand {

    static CreateValidatorsCommand() {
        DafnyOptions.RegisterLegacyBinding(AddPureCopiesOption, (options, value) => {
            options.AddPureCopiesOption = value;
        });

        DafnyOptions.RegisterLegacyBinding(ValidatePure, (options, value) => {
            options.ValidatePure = value;
        });

        DafnyOptions.RegisterLegacyBinding(ValidateLemmas, (options, value) => {
            options.ValidateLemmas = value;
        });

        DooFile.RegisterNoChecksNeeded(AddPureCopiesOption, false);
        DooFile.RegisterNoChecksNeeded(ValidatePure, false);
        DooFile.RegisterNoChecksNeeded(ValidateLemmas, false);
    }


    public static IEnumerable<Option> Options => new Option[] {
        AddPureCopiesOption,
        ValidatePure,
        ValidateLemmas,
    }.Concat(DafnyCommands.ParserOptions);

    public static readonly Option<bool> AddPureCopiesOption = new("--addPureCopies", () => false,
        @"Add copies of functions and predicates to validation.");

    public static readonly Option<bool> ValidatePure = new("--validatePure",
        @"Validate functions and predicates.") {
    };

    public static readonly Option<bool> ValidateLemmas = new("--validateLemmas",
        @"Validate lemmas.") {
    };

    public static Command Create() {
        var result = new Command("validate", @"Create validators for dafny code.");
        result.AddArgument(DafnyCommands.FilesArgument);

        foreach (var option in Options) {
            result.AddOption(option);
        }

        DafnyNewCli.SetHandlerUsingDafnyOptionsContinuation(result, async (options, _) => {
            options.AllowSourceFolders = true;
            // System.Console.WriteLine("Creating validators...");
            // System.Console.WriteLine("Add Pure Copies: " + options.AddPureCopiesOption);
            // System.Console.WriteLine("Validate Pure: " + options.ValidatePure);
            // System.Console.WriteLine("Validate Lemmas: " + options.ValidateLemmas);

            var exitValue = await DoValidation(options);

            return (int)exitValue;
        });

        return result;
    }


    
    public static async Task<ExitValue> DoValidation(DafnyOptions options) {
        var (code, dafnyFiles, _) = await SynchronousCliCompilation.GetDafnyFiles(options);
        if (code != 0) {
            return code;
        }
        var errorWriter = options.ErrorWriter;
        var dafnyFileNames = DafnyFile.FileNames(dafnyFiles);
        string programName = dafnyFileNames.Count == 1 ? dafnyFileNames[0] : "the_program";

        var exitValue = ExitValue.SUCCESS;
        Contract.Assert(dafnyFiles.Count > 0 || options.SourceFolders.Count > 0);
        var folderFiles = options.SourceFolders.Select(folderPath => GetFilesForFolder(options, folderPath)).SelectMany(x => x);
        dafnyFiles = dafnyFiles.Concat(folderFiles).ToList();
        
        foreach (var file in dafnyFiles) {
            var dafnyFile = file;
            string tempFileName = null;
            if (dafnyFile.Uri.Scheme == "stdin") {
                tempFileName = Path.GetTempFileName() + ".dfy";
                SynchronousCliCompilation.WriteFile(tempFileName, await Console.In.ReadToEndAsync());
                dafnyFile = DafnyFile.HandleDafnyFile(OnDiskFileSystem.Instance, new ConsoleErrorReporter(options), options, new Uri(tempFileName), Token.NoToken);
            }
            
            var content = dafnyFile.GetContent();
            var originalText = await content.ReadToEndAsync();
            content.Close(); // Manual closing because we want to overwrite
            dafnyFile.GetContent = () => new StringReader(originalText);
            // Might not be totally optimized but let's do that for now
            var (dafnyProgram, err) = await DafnyMain.Parse(new List<DafnyFile> { dafnyFile }, programName, options);

            // var err1 = DafnyMain.Resolve(dafnyProgram);

            // Console.WriteLine($"Before: {dafnyProgram.DefaultModuleDef.PrefixNamedModules.Count}");
            // Console.WriteLine($"Before: {dafnyProgram.DefaultModuleDef.TopLevelDecls.Count}");
            
            // var tw = new StringWriter {
            //     NewLine = "\n"
            // };
            // var pr = new Printer(tw, dafnyProgram.Options, PrintModes.Serialization);
            // pr.PrintProgram(dafnyProgram, false);

            // var cloner = new ClonerButDropMethodBodies(false);
            var cloner = new ClonerValidator(options, false);
            var cloner1 = new DeepModuleSignatureCloner(false);
            cloner.CloneLiteralModuleDefinition = true;
            cloner1.CloneLiteralModuleDefinition = true;


            var module = dafnyProgram.DefaultModule;
            var cloned_module_Valid = new ModuleDefinition(cloner, module.ModuleDef, new Name("Valid"));
            
            var path = new List<Name>();
            path.Add(new Name("Gen"));   

            cloned_module_Valid.ResolvedPrefixNamedModules.Add(new AliasModuleDecl(
                options,// DafnyOptions options, 
                RangeToken.NoToken, // RangeToken rangeToken, 
                new ModuleQualifiedId(path),// ModuleQualifiedId path, 
                new Name("Gen"),// Name name,
                cloned_module_Valid,// ModuleDefinition parent, 
                true,// bool opened, 
                new List<IToken>(),// List<IToken> exports,
                Guid.NewGuid()// Guid cloneId
            ));

            var module1 = dafnyProgram.DefaultModule;
            var cloned_module_Gen = new ModuleDefinition(cloner1, module1.ModuleDef, new Name("Gen"));
            
            var prefixNamedModule_Valid = new PrefixNameModule(options, cloned_module_Valid.PrefixIds, new LiteralModuleDecl(options, cloned_module_Valid, module.ModuleDef, Guid.NewGuid()));
            var prefixNamedModule_Gen = new PrefixNameModule(options, cloned_module_Gen.PrefixIds, new LiteralModuleDecl(options, cloned_module_Gen, module1.ModuleDef, Guid.NewGuid()));
            dafnyProgram.DefaultModuleDef.PrefixNamedModules = new List<PrefixNameModule>();
            // dafnyProgram.DefaultModuleDef.PrefixNamedModules.Add(prefixNamedModule_Gen);
            dafnyProgram.DefaultModuleDef.PrefixNamedModules.Add(prefixNamedModule_Valid);
            
            var tw1 = new StringWriter {
                NewLine = "\n"
            };
            var pr1 = new Printer(tw1, dafnyProgram.Options, PrintModes.Validation);
            pr1.PrintProgram(dafnyProgram, false);
            SynchronousCliCompilation.WriteFile(dafnyFile.FilePath, tw1.ToString());

        }

        return exitValue;
    }


    public static IEnumerable<DafnyFile> GetFilesForFolder(DafnyOptions options, string folderPath) {
        return Directory.GetFiles(folderPath, "*.dfy", SearchOption.AllDirectories)
        .Select(name => DafnyFile.HandleDafnyFile(OnDiskFileSystem.Instance,
            new ConsoleErrorReporter(options), options, new Uri(name), Token.Cli));
    }
}