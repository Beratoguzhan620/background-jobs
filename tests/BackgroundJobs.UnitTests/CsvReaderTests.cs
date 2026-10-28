using BackgroundJobs.Core.Imports;

namespace BackgroundJobs.UnitTests;

public class CsvReaderTests
{
    private static async Task<List<(int Line, string[] Fields)>> ReadAllAsync(string text)
    {
        var reader = new CsvReader(new StringReader(text));
        var records = new List<(int, string[])>();
        while (await reader.ReadRecordAsync() is { } fields)
        {
            records.Add((reader.LineNumber, fields));
        }

        return records;
    }

    [Fact]
    public async Task Reads_plain_fields_with_lf_or_crlf()
    {
        var records = await ReadAllAsync("a,b,c\r\n1,2,3\n4,,6");

        Assert.Equal([["a", "b", "c"], ["1", "2", "3"], ["4", "", "6"]], records.Select(r => r.Fields));
        Assert.Equal([1, 2, 3], records.Select(r => r.Line));
    }

    [Fact]
    public async Task Handles_quotes_commas_doubled_quotes_and_line_breaks_inside_quotes()
    {
        var records = await ReadAllAsync("\"a,b\",\"say \"\"hi\"\"\",\"two\nlines\"\nnext,row,here\n");

        Assert.Equal(["a,b", "say \"hi\"", "two\nlines"], records[0].Fields);
        Assert.Equal(["next", "row", "here"], records[1].Fields);
        Assert.Equal(3, records[1].Line);
    }

    [Fact]
    public async Task A_trailing_newline_does_not_add_an_empty_record()
    {
        Assert.Single(await ReadAllAsync("a,b\n"));
        Assert.Empty(await ReadAllAsync(""));
    }

    [Fact]
    public async Task Reads_records_larger_than_its_buffer()
    {
        var big = new string('x', 50_000);

        var records = await ReadAllAsync($"\"{big}\",end\nnext\n");

        Assert.Equal(big, records[0].Fields[0]);
        Assert.Equal(["next"], records[1].Fields);
    }
}
