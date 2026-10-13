namespace VSAgentBridge.Init;

/// <summary>
/// The questions init asks in the console. At the end of input (no one to answer, for example redirected
/// input) a yes/no question is answered "no" and a choice "none", so that nothing is changed unasked.
/// </summary>
internal sealed class ConsolePrompt
{
    private readonly TextReader _input;

    public ConsolePrompt(TextReader input, TextWriter output)
    {
        _input = input;
        Output = output;
    }

    /// <summary>Where init writes everything else: the same output as the questions.</summary>
    public TextWriter Output { get; }

    public static ConsolePrompt ForConsole() => new(Console.In, Console.Out);

    /// <summary>Writes <paramref name="question"/> and returns the line typed, trimmed; null at the end of input.</summary>
    public string? AskLine(string question)
    {
        Output.Write(question + " ");
        Output.Flush();
        return _input.ReadLine()?.Trim();
    }

    /// <summary>
    /// A yes/no question with "[Y/n]" or "[y/N]" added; Enter gives <paramref name="defaultYes"/>.
    /// Asks again on an answer it does not understand; "no" at the end of input.
    /// </summary>
    public bool AskYesNo(string question, bool defaultYes = true)
    {
        var hint = defaultYes ? "[Y/n]" : "[y/N]";
        while (true)
        {
            var answer = AskLine($"{question} {hint}");
            if (answer == null)
            {
                Output.WriteLine();
                return false;
            }

            switch (answer.ToLowerInvariant())
            {
                case "":
                    return defaultYes;
                case "y":
                case "yes":
                    return true;
                case "n":
                case "no":
                    return false;
            }

            Output.WriteLine("Please answer y or n.");
        }
    }

    /// <summary>
    /// Writes the numbered <paramref name="choices"/> and asks for a number, with no default.
    /// Returns 1..choices.Count; null at the end of input.
    /// </summary>
    public int? AskChoice(IReadOnlyList<string> choices)
    {
        for (var i = 0; i < choices.Count; i++)
            Output.WriteLine($"  {i + 1}. {choices[i]}");

        var range = string.Join("/", Enumerable.Range(1, choices.Count));
        while (true)
        {
            var answer = AskLine($"Choose [{range}]:");
            if (answer == null)
            {
                Output.WriteLine();
                return null;
            }

            if (int.TryParse(answer, out var number) && number >= 1 && number <= choices.Count)
                return number;

            Output.WriteLine($"Please type a number from 1 to {choices.Count}.");
        }
    }
}