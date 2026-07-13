namespace CompositeGenerator;

internal sealed record CompositeRequest(int StartDigits, int EndDigits, int Count)
{
    public static CompositeRequest Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new FormatException("A digit count or --range M N is required.");
        }

        int? singleDigits = null;
        int? rangeStart = null;
        int? rangeEnd = null;
        var count = 1;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--range", StringComparison.OrdinalIgnoreCase))
            {
                if (rangeStart is not null || singleDigits is not null)
                {
                    throw new FormatException("Use either one digit count or --range M N, not both.");
                }

                if (i + 2 >= args.Length)
                {
                    throw new FormatException("--range requires two natural-number values.");
                }

                rangeStart = ParseNatural(args[++i], "range start");
                rangeEnd = ParseNatural(args[++i], "range end");
                continue;
            }

            if (arg.Equals("--count", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    throw new FormatException("--count requires a natural-number value.");
                }

                count = ParseNatural(args[++i], "count");
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new FormatException($"Unknown option '{arg}'.");
            }

            if (singleDigits is not null || rangeStart is not null)
            {
                throw new FormatException("Only one positional digit count is allowed.");
            }

            singleDigits = ParseNatural(arg, "digit count");
        }

        if (rangeStart is { } start && rangeEnd is { } end)
        {
            if (start > end)
            {
                throw new FormatException("--range start must be less than or equal to end.");
            }

            return new CompositeRequest(start, end, count);
        }

        if (singleDigits is { } digits)
        {
            return new CompositeRequest(digits, digits, count);
        }

        throw new FormatException("A digit count or --range M N is required.");
    }

    private static int ParseNatural(string text, string name)
    {
        if (!int.TryParse(text, out var value) || value < 1)
        {
            throw new FormatException($"{name} must be a natural number.");
        }

        return value;
    }
}
