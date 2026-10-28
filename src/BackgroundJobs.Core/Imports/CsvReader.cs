using System.Text;

namespace BackgroundJobs.Core.Imports;

/// <summary>
/// Streaming RFC 4180 reader: quoted fields, doubled quotes, line breaks inside quotes, CRLF or LF.
/// It reads through a fixed buffer one record at a time, so a 50 MB file never sits in memory.
/// </summary>
public sealed class CsvReader(TextReader reader)
{
    private const int BufferSize = 16 * 1024;

    private readonly char[] _buffer = new char[BufferSize];
    private readonly StringBuilder _field = new();
    private int _length;
    private int _position;
    private int _currentLine = 1;

    /// <summary>Line number where the last returned record started (1-based).</summary>
    public int LineNumber { get; private set; }

    public async Task<string[]?> ReadRecordAsync(CancellationToken ct = default)
    {
        var fields = new List<string>();
        var inQuotes = false;
        var any = false;
        LineNumber = _currentLine;
        _field.Clear();

        while (true)
        {
            if (_position >= _length)
            {
                ct.ThrowIfCancellationRequested();
                _length = await reader.ReadAsync(_buffer.AsMemory(), ct);
                _position = 0;
                if (_length == 0)
                {
                    if (!any)
                    {
                        return null;
                    }

                    fields.Add(_field.ToString());
                    return fields.ToArray();
                }
            }

            var c = _buffer[_position++];
            any = true;

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (await PeekAsync(ct) == '"')
                    {
                        _position++;
                        _field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        _currentLine++;
                    }

                    _field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when _field.Length == 0:
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(_field.ToString());
                    _field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    _currentLine++;
                    fields.Add(_field.ToString());
                    return fields.ToArray();
                default:
                    _field.Append(c);
                    break;
            }
        }
    }

    private async ValueTask<int> PeekAsync(CancellationToken ct)
    {
        if (_position >= _length)
        {
            _length = await reader.ReadAsync(_buffer.AsMemory(), ct);
            _position = 0;
            if (_length == 0)
            {
                return -1;
            }
        }

        return _buffer[_position];
    }
}
