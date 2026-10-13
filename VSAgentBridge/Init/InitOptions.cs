namespace VSAgentBridge.Init;

/// <summary>The command line of "vsagentbridge init": which agent and the options. No questions, no file access.</summary>
internal sealed class InitOptions
{
    public const string Claude = "claude";
    public const string OpenCode = "opencode";

    public const string Usage =
        "vsagentbridge init claude [--roots \"D:\\A;D:\\B\"] [-g] [--no-marketplace]\n" +
        "vsagentbridge init opencode [--roots \"D:\\A;D:\\B\"] [-g]";

    private InitOptions(string harness, bool globalRules, string? roots, bool noMarketplace)
    {
        Harness = harness;
        GlobalRules = globalRules;
        Roots = roots;
        NoMarketplace = noMarketplace;
    }

    /// <summary><see cref="Claude"/> or <see cref="OpenCode"/>.</summary>
    public string Harness { get; }

    /// <summary>-g: install the rules globally without asking.</summary>
    public bool GlobalRules { get; }

    /// <summary>--roots as given (parsed later with WorkspaceRoots); null: ask in the console.</summary>
    public string? Roots { get; }

    /// <summary>--no-marketplace (Claude Code only): use CLAUDE_CODE_PLUGIN_DIRS right away.</summary>
    public bool NoMarketplace { get; }

    /// <summary>Parses the arguments after "init". Returns false with a message for the user if they are wrong.</summary>
    public static bool TryParse(IReadOnlyList<string> args, out InitOptions? options, out string? error)
    {
        options = null;
        if (args.Count == 0)
        {
            error = "Which agent to set up: claude or opencode.";
            return false;
        }

        var harness = args[0].ToLowerInvariant();
        if (harness != Claude && harness != OpenCode)
        {
            error = $"Unknown agent \"{args[0]}\": use claude or opencode.";
            return false;
        }

        var globalRules = false;
        var noMarketplace = false;
        string? roots = null;
        var seen = new HashSet<string>();

        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            string name;
            string? inlineValue = null;

            var equals = arg.IndexOf('=');
            if (arg.StartsWith("--", StringComparison.Ordinal) && equals > 0)
            {
                name = arg.Substring(0, equals).ToLowerInvariant();
                inlineValue = arg.Substring(equals + 1);
            }
            else
            {
                name = arg.ToLowerInvariant();
            }

            if (name == "--global")
                name = "-g";

            if (!seen.Add(name) && (name == "-g" || name == "--roots" || name == "--no-marketplace"))
            {
                error = $"\"{name}\" is given twice.";
                return false;
            }

            switch (name)
            {
                case "-g" when inlineValue == null:
                    globalRules = true;
                    break;

                case "--no-marketplace" when inlineValue == null:
                    if (harness != Claude)
                    {
                        error = "--no-marketplace is for claude only.";
                        return false;
                    }
                    noMarketplace = true;
                    break;

                case "--roots":
                    if (inlineValue == null)
                    {
                        if (i + 1 >= args.Count || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        {
                            error = "--roots needs a value: folders separated by ';'.";
                            return false;
                        }
                        inlineValue = args[++i];
                    }
                    roots = inlineValue;
                    break;

                default:
                    error = $"Unknown option \"{arg}\".";
                    return false;
            }
        }

        options = new InitOptions(harness, globalRules, roots, noMarketplace);
        error = null;
        return true;
    }
}