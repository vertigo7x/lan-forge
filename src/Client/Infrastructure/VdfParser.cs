using System.Text;

namespace LanForge.Client.Infrastructure;

/// <summary>
/// Parser mínimo del formato VDF (Valve KeyValues): pares "clave" "valor" y
/// secciones anidadas con llaves. Los valores repetidos conservan el primero.
/// Devuelve diccionarios anidados (string para hojas, VdfObject para secciones).
/// </summary>
public static class VdfParser
{
    public sealed class VdfObject : Dictionary<string, object>
    {
        public VdfObject() : base(StringComparer.Ordinal)
        {
        }

        /// <summary>Valor de texto de una hoja, o null si no existe o es una sección.</summary>
        public string? GetString(string key) =>
            TryGetValue(key, out var value) ? value as string : null;

        /// <summary>Sección anidada, o null si no existe o es una hoja.</summary>
        public VdfObject? GetSection(string key) =>
            TryGetValue(key, out var value) ? value as VdfObject : null;
    }

    /// <summary>Parsea un documento VDF. Lanza FormatException si está mal formado.</summary>
    public static VdfObject Parse(string content)
    {
        var reader = new Reader(content);
        var root = new VdfObject();
        ReadSection(reader, root);
        return root;
    }

    /// <summary>Intenta parsear; devuelve null si el contenido no es VDF válido.</summary>
    public static VdfObject? TryParse(string content)
    {
        try
        {
            return Parse(content);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static void ReadSection(Reader reader, VdfObject target)
    {
        while (true)
        {
            var token = reader.NextToken();
            if (token is null or "}")
            {
                return;
            }

            if (token == "{")
            {
                throw new FormatException("Llave de apertura sin clave previa.");
            }

            var next = reader.PeekToken();
            if (next == "{")
            {
                reader.NextToken();
                var section = new VdfObject();
                ReadSection(reader, section);
                target.TryAdd(token, section);
            }
            else if (next is not null and not "}")
            {
                reader.NextToken();
                target.TryAdd(token, next);
            }
            else
            {
                throw new FormatException($"Clave '{token}' sin valor.");
            }
        }
    }

    private sealed class Reader
    {
        private readonly string _content;
        private int _pos;
        private string? _peeked;

        public Reader(string content)
        {
            _content = content;
        }

        public string? PeekToken()
        {
            _peeked ??= NextToken();
            return _peeked;
        }

        public string? NextToken()
        {
            if (_peeked is not null)
            {
                var token = _peeked;
                _peeked = null;
                return token;
            }

            SkipWhitespaceAndComments();
            if (_pos >= _content.Length)
            {
                return null;
            }

            var current = _content[_pos];
            if (current is '{' or '}')
            {
                _pos++;
                return current.ToString();
            }

            if (current == '"')
            {
                return ReadQuoted();
            }

            // Token sin comillas (tolerante con VDF laxo).
            var start = _pos;
            while (_pos < _content.Length && !char.IsWhiteSpace(_content[_pos]) && _content[_pos] is not '{' and not '}')
            {
                _pos++;
            }

            return _content[start.._pos];
        }

        private string ReadQuoted()
        {
            _pos++; // comilla inicial
            var builder = new StringBuilder();
            while (_pos < _content.Length)
            {
                var c = _content[_pos++];
                if (c == '"')
                {
                    return builder.ToString();
                }

                if (c == '\\' && _pos < _content.Length)
                {
                    var escaped = _content[_pos++];
                    builder.Append(escaped switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        _ => escaped,
                    });
                }
                else
                {
                    builder.Append(c);
                }
            }

            throw new FormatException("Cadena sin cerrar.");
        }

        private void SkipWhitespaceAndComments()
        {
            while (_pos < _content.Length)
            {
                if (char.IsWhiteSpace(_content[_pos]))
                {
                    _pos++;
                    continue;
                }

                if (_content[_pos] == '/' && _pos + 1 < _content.Length && _content[_pos + 1] == '/')
                {
                    while (_pos < _content.Length && _content[_pos] != '\n')
                    {
                        _pos++;
                    }

                    continue;
                }

                return;
            }
        }
    }
}
