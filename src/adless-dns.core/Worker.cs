using ARSoft.Tools.Net.Dns;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace adless_dns.core
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private DnsServer _ipv4DnsServer, _ipv6DnsServer;
        private DnsClient _dnsClient;
        private Dictionary<string, string> _hosts;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
            _hosts = new Dictionary<string, string>();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _ipv4DnsServer = new DnsServer(IPAddress.Any, 10, 10);
                _ipv6DnsServer = new DnsServer(IPAddress.IPv6Any, 10, 10);
                _dnsClient = new DnsClient(new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Parse("1.1.1.1") }, 10000);
                _ipv4DnsServer.QueryReceived += DnsServer_QueryReceived;
                _ipv6DnsServer.QueryReceived += DnsServer_QueryReceived;
                _ipv4DnsServer.Start();
                _ipv6DnsServer.Start();
                await loadHosts();
                while (!stoppingToken.IsCancellationRequested)
                    await Task.Delay(1000, stoppingToken);
                _ipv4DnsServer.Stop();
                _ipv6DnsServer.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExecuteAsync");
            }
        }

        private async Task loadHosts()
        {
            Dictionary<string, string> hosts = new Dictionary<string, string>();
            using (var fs = new FileStream("hosts", FileMode.Open))
            using (var reader = new StreamReader(fs))
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (!line.StartsWith("#"))
                    {
                        var data = line.Split(' ');
                        if (data.Length == 2)
                        {
                            hosts.TryAdd(data[1].ToLower(), data[0]);
                            hosts.TryAdd(data[1].ToLower() + ".", data[0]);
                        }
                    }
                }
            }
            _hosts = hosts;
        }

        private async Task DnsServer_QueryReceived(object sender, QueryReceivedEventArgs eventArgs)
        {
            DnsMessage message = eventArgs.Query as DnsMessage;

            if (message == null)
                return;

            DnsMessage response = message.CreateResponseInstance();

            if ((message.Questions.Count == 1))
            {
                // send query to upstream server
                DnsQuestion question = message.Questions[0];
                DnsMessage upstreamResponse = await _dnsClient.ResolveAsync(question.Name, question.RecordType, question.RecordClass);

                // if got an answer, copy it to the message sent to the client
                if (upstreamResponse != null)
                {
                    foreach (DnsRecordBase record in (upstreamResponse.AnswerRecords))
                    {
                        response.AnswerRecords.Add(record);
                    }
                    foreach (DnsRecordBase record in (upstreamResponse.AdditionalRecords))
                    {
                        response.AdditionalRecords.Add(record);
                    }
                }

                string type = "Default";
                if (_hosts.TryGetValue(question.Name.ToString().ToLower(), out string result))
                {
                    if (question.RecordType == RecordType.A)
                    {
                        var record = new ARecord(question.Name, 10, IPAddress.Parse(result));
                        response.AnswerRecords.Clear();
                        response.AnswerRecords.Add(record);
                        type = "Override";
                    }
                    else if (question.RecordType == RecordType.Aaaa && result == "0.0.0.0")
                    {
                        result = "::/0";
                        var record = new AaaaRecord(question.Name, 10, IPAddress.Parse(result));
                        response.AnswerRecords.Clear();
                        response.AnswerRecords.Add(record);
                        type = "Override";
                    }
                }
                var answer = response.AnswerRecords.FirstOrDefault();
                if (answer != null)
                {
                    _logger.LogDebug($"{{{type}}} [{question.Name}] - [{answer}]");
                }

                response.ReturnCode = ReturnCode.NoError;

                // set the response
                eventArgs.Response = response;
            }
        }
    }
}
