using CompositeGenerator;

const string Usage = "usage: CompositeGenerator X [--count A] | CompositeGenerator --range M N [--count A]";

try
{
    if (args.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase) || a.Equals("-h", StringComparison.OrdinalIgnoreCase)))
    {
        Console.WriteLine(Usage);
        return 0;
    }

    var request = CompositeRequest.Parse(args);
    for (var digits = request.StartDigits; digits <= request.EndDigits; digits++)
    {
        for (var i = 0; i < request.Count; i++)
        {
            Console.WriteLine(SemiprimeGenerator.Generate(digits));
        }
    }

    return 0;
}
catch (Exception ex) when (ex is FormatException or ArgumentException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine(Usage);
    return 1;
}
