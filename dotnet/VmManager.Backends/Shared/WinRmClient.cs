using System.Text;
using System.Xml.Linq;

namespace VmManager.Backends.Shared;

public class WinRmClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _endpoint;

    private static readonly XNamespace Soap = "http://www.w3.org/2003/05/soap-envelope";
    private static readonly XNamespace Wsman = "http://schemas.dmtf.org/wbem/wsman/1/wsman.xsd";
    private static readonly XNamespace Wsa = "http://schemas.xmlsoap.org/ws/2004/08/addressing";
    private static readonly XNamespace Shell =
        "http://schemas.microsoft.com/wbem/wsman/1/windows/shell";

    private static readonly string ShellResourceUri =
        "http://schemas.microsoft.com/wbem/wsman/1/windows/shell/cmd";

    public WinRmClient(string host, string username, string password)
    {
        _endpoint = $"http://{host}:5985/wsman";

        string credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{username}:{password}")
        );
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<WinRmResult> RunPowerShellAsync(string script)
    {
        string shellId = await CreateShellAsync();
        try
        {
            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            string commandId = await ExecuteCommandAsync(
                shellId,
                "powershell",
                $"-EncodedCommand {encodedCommand}"
            );

            return await ReceiveOutputAsync(shellId, commandId);
        }
        finally
        {
            try
            {
                await DeleteShellAsync(shellId);
            }
            catch { }
        }
    }

    private async Task<string> CreateShellAsync()
    {
        XElement body = new XElement(
            Shell + "Shell",
            new XElement(Shell + "InputStreams", "stdin"),
            new XElement(Shell + "OutputStreams", "stdout stderr")
        );

        XElement envelope = BuildEnvelope(
            ShellResourceUri,
            "http://schemas.xmlsoap.org/ws/2004/09/transfer/Create",
            body,
            new XElement(
                Wsman + "OptionSet",
                new XElement(Wsman + "Option", new XAttribute("Name", "WINRS_NOPROFILE"), "FALSE"),
                new XElement(Wsman + "Option", new XAttribute("Name", "WINRS_CODEPAGE"), "65001")
            )
        );

        XDocument response = await SendAsync(envelope);
        XElement? shellIdEl = response.Descendants(Shell + "ShellId").FirstOrDefault();
        return shellIdEl?.Value
            ?? throw new InvalidOperationException("No ShellId in create shell response");
    }

    private async Task<string> ExecuteCommandAsync(string shellId, string command, string arguments)
    {
        XElement body = new XElement(
            Shell + "CommandLine",
            new XElement(Shell + "Command", command),
            new XElement(Shell + "Arguments", arguments)
        );

        XElement envelope = BuildEnvelope(
            ShellResourceUri,
            "http://schemas.microsoft.com/wbem/wsman/1/windows/shell/Command",
            body,
            new XElement(
                Wsman + "OptionSet",
                new XElement(
                    Wsman + "Option",
                    new XAttribute("Name", "WINRS_CONSOLEMODE_STDIN"),
                    "TRUE"
                ),
                new XElement(
                    Wsman + "Option",
                    new XAttribute("Name", "WINRS_SKIP_CMD_SHELL"),
                    "FALSE"
                )
            ),
            shellId
        );

        XDocument response = await SendAsync(envelope);
        XElement? commandIdEl = response.Descendants(Shell + "CommandId").FirstOrDefault();
        return commandIdEl?.Value
            ?? throw new InvalidOperationException("No CommandId in execute response");
    }

    private async Task<WinRmResult> ReceiveOutputAsync(string shellId, string commandId)
    {
        StringBuilder stdout = new StringBuilder();
        StringBuilder stderr = new StringBuilder();
        int exitCode = -1;

        while (true)
        {
            XElement body = new XElement(
                Shell + "Receive",
                new XElement(
                    Shell + "DesiredStream",
                    new XAttribute("CommandId", commandId),
                    "stdout stderr"
                )
            );

            XElement envelope = BuildEnvelope(
                ShellResourceUri,
                "http://schemas.microsoft.com/wbem/wsman/1/windows/shell/Receive",
                body,
                selectorShellId: shellId
            );

            XDocument response = await SendAsync(envelope);

            foreach (XElement stream in response.Descendants(Shell + "Stream"))
            {
                string streamName = stream.Attribute("Name")?.Value ?? "";
                string content = stream.Value;
                if (string.IsNullOrEmpty(content))
                    continue;

                byte[] decoded = Convert.FromBase64String(content);
                string text = Encoding.UTF8.GetString(decoded);

                if (streamName == "stdout")
                    stdout.Append(text);
                else if (streamName == "stderr")
                    stderr.Append(text);
            }

            XElement? exitCodeEl = response.Descendants(Shell + "ExitCode").FirstOrDefault();
            if (exitCodeEl != null)
            {
                exitCode = int.Parse(exitCodeEl.Value);
                break;
            }

            XElement? stateEl = response.Descendants(Shell + "CommandState").FirstOrDefault();
            string? state = stateEl?.Attribute("State")?.Value;
            if (state != null && state.EndsWith("Done"))
            {
                exitCodeEl = response.Descendants(Shell + "ExitCode").FirstOrDefault();
                exitCode = exitCodeEl != null ? int.Parse(exitCodeEl.Value) : 0;
                break;
            }
        }

        return new WinRmResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    private async Task DeleteShellAsync(string shellId)
    {
        XElement envelope = BuildEnvelope(
            ShellResourceUri,
            "http://schemas.xmlsoap.org/ws/2004/09/transfer/Delete",
            selectorShellId: shellId
        );

        await SendAsync(envelope);
    }

    private XElement BuildEnvelope(
        string resourceUri,
        string action,
        XElement? body = null,
        XElement? optionSet = null,
        string? selectorShellId = null
    )
    {
        XElement header = new XElement(
            Soap + "Header",
            new XElement(Wsa + "To", _endpoint),
            new XElement(
                Wsman + "ResourceURI",
                new XAttribute(Soap + "mustUnderstand", "true"),
                resourceUri
            ),
            new XElement(
                Wsa + "ReplyTo",
                new XElement(
                    Wsa + "Address",
                    "http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous"
                )
            ),
            new XElement(Wsa + "Action", new XAttribute(Soap + "mustUnderstand", "true"), action),
            new XElement(
                Wsman + "MaxEnvelopeSize",
                new XAttribute(Soap + "mustUnderstand", "true"),
                "512000"
            ),
            new XElement(Wsa + "MessageID", $"uuid:{Guid.NewGuid()}"),
            new XElement(Wsman + "OperationTimeout", "PT60S")
        );

        if (selectorShellId != null)
        {
            header.Add(
                new XElement(
                    Wsman + "SelectorSet",
                    new XElement(
                        Wsman + "Selector",
                        new XAttribute("Name", "ShellId"),
                        selectorShellId
                    )
                )
            );
        }

        if (optionSet != null)
            header.Add(optionSet);

        XElement envelope = new XElement(
            Soap + "Envelope",
            header,
            new XElement(Soap + "Body", body)
        );

        return envelope;
    }

    private async Task<XDocument> SendAsync(XElement envelope)
    {
        StringContent content = new StringContent(
            envelope.ToString(),
            Encoding.UTF8,
            "application/soap+xml"
        );

        HttpResponseMessage response = await _http.PostAsync(_endpoint, content);
        string responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"WinRM request failed ({response.StatusCode}): {responseBody}"
            );
        }

        return XDocument.Parse(responseBody);
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}

public record WinRmResult(int ExitCode, string StdOut, string StdErr);
