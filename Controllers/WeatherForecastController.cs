using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Net.Mail;
using System.Net;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.IO;
using MattermostBackend.Context;
using MattermostBackend.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.Net.Http;
using System;

namespace MattermostBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MattermostController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly SmtpSettings _smtpSettings;
        private readonly IConfiguration _configuration;
      
        public MattermostController(AppDbContext context,HttpClient httpClient, IOptions<SmtpSettings> smtpSettings, IConfiguration configuration)
        {
            _context = context;
            _httpClient = httpClient;
            _smtpSettings = smtpSettings.Value;
            _configuration = configuration;
        }
        [HttpPost("upload")]
        public async Task<IActionResult> UploadTicket([FromBody] CreateTicketDto dto)
        {
            string nextTicketNo = await GenerateNextTicketNo();

            var ticket = new Ticket
            {
                TicketNo = nextTicketNo,
                Topic = dto.Topic,
                Detail = dto.Detail,
                Severity = dto.Severity,
                Location = dto.Location,
                ChannelName = dto.ChannelName,
                Status = true
            };

            _context.Tickets.Add(ticket);
            await _context.SaveChangesAsync();

            // Build message for Mattermost
            string message = $"**New Ticket Created**\n" +
                             $"**Ticket No:** {ticket.TicketNo}\n" +
                             $"**Topic:** {ticket.Topic}\n" +
                             $"**Detail:** {ticket.Detail}\n" +
                             $"**Severity:** {ticket.Severity}\n" +
                             $"**Location:** {ticket.Location}";
            //string message = $"New ticket Created";

            // Send to Mattermost dto.TeamName
            await SendToMattermost("ruhama",ticket.ChannelName, message);

            return Ok(new { message = "Ticket created", ticketId = ticket.Id, ticketNo = ticket.TicketNo });
        }
        

        [HttpPost("tickets/close")]
        public async Task<IActionResult> CloseTicketFromWebhook()
        {
            var form = await Request.ReadFormAsync();

            foreach (var key in form.Keys)
            {
                Console.WriteLine($"Key: {key}, Value: {form[key]}");
            }

            var text = form["text"].ToString();
            var command = form["command"].ToString();
            var responseUrl = form["response_url"].ToString();

            if (!command.StartsWith("/closeticket", StringComparison.OrdinalIgnoreCase))
                return Ok(); // ignore if not our command

            var match = Regex.Match(text, @"TIC-\d+");
            if (!match.Success)
                return BadRequest("Please provide a valid ticket number like TIC-123");

            string ticketNo = match.Value;

            var ticket = await _context.Tickets.FirstOrDefaultAsync(t => t.TicketNo == ticketNo);
            
            if (ticket == null)
                return NotFound($"Ticket {ticketNo} not found.");
            if (ticket.Status == false)
                return BadRequest($"Ticket: {ticketNo} has been closed");

            ticket.Status = false;
            await _context.SaveChangesAsync();

            // ✅ Send a message to Mattermost using response_url
            using (var httpClient = new HttpClient())
            {
                var message = new
                {
                    response_type = "in_channel", // visible to everyone in the channel
                    text = $"Ticket **{ticketNo}** has been closed successfully by `{form["user_name"]}`."
                };

                var content = new StringContent(JsonConvert.SerializeObject(message), Encoding.UTF8, "application/json");
                await httpClient.PostAsync(responseUrl, content);
            }

            return Ok(); // don't return text here, as message is sent via response_url
        }


        [HttpGet("get-channels/{teamId}")]
        public async Task<IActionResult> GetChannelsForTeam(string teamId)
        {
            var token = _configuration["Mattermost:PAToken"];
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://matermost.finosys-sbs.com/api/v4/users/me/teams/{teamId}/channels");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = _httpClient.Send(request); // Assuming you use IHttpClientFactory
            //var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, "Failed to fetch channels from Mattermost");
            }

            var content = await response.Content.ReadAsStringAsync();
            var channels = System.Text.Json.JsonSerializer.Deserialize<List<ChannelLookupDto>>(content, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                  PropertyNameCaseInsensitive = true
            });
            if (channels == null || channels.Count == 0)
            {
                return NotFound("No channels found");
            }

            var result = channels
               .Where(channel => channel.Type == "P" || channel.Type == "O")
                .Select(channel => new
            {
                Id = channel.Id,
                ChannelName = channel.Name,
                Type = channel.Type
            });
            return Ok(result);
        }
        
        public class ChannelLookupDto
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("create_at")]
            public long CreateAt { get; set; }

            [JsonPropertyName("update_at")]
            public long UpdateAt { get; set; }

            [JsonPropertyName("delete_at")]
            public long DeleteAt { get; set; }

            [JsonPropertyName("team_id")]
            public string TeamId { get; set; }

            [JsonPropertyName("type")]
            public string Type { get; set; }

            [JsonPropertyName("display_name")]
            public string DisplayName { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("header")]
            public string Header { get; set; }

            [JsonPropertyName("purpose")]
            public string Purpose { get; set; }

            [JsonPropertyName("last_post_at")]
            public long LastPostAt { get; set; }

            [JsonPropertyName("total_msg_count")]
            public int TotalMsgCount { get; set; }

            [JsonPropertyName("extra_update_at")]
            public long ExtraUpdateAt { get; set; }

            [JsonPropertyName("creator_id")]
            public string CreatorId { get; set; }

            [JsonPropertyName("scheme_id")]
            public string SchemeId { get; set; }

            [JsonPropertyName("props")]
            public Dictionary<string, object> Props { get; set; }

            [JsonPropertyName("group_constrained")]
            public bool? GroupConstrained { get; set; }

            [JsonPropertyName("shared")]
            public bool? Shared { get; set; }

            [JsonPropertyName("total_msg_count_root")]
            public int TotalMsgCountRoot { get; set; }

            [JsonPropertyName("policy_id")]
            public string PolicyId { get; set; }

            [JsonPropertyName("last_root_post_at")]
            public long LastRootPostAt { get; set; }
        }

        private async Task SendToMattermost(string TeamName,string Channel, string message)
        {
            var payload = new
            {
                channel = Channel,
                username = "TicketBot",
                text = message
            };
            string webhookUrl = TeamName switch
            {
                "sina-healthcare" => "https://matermost.finosys-sbs.com/hooks/wtbmppdtqfgydmymzrxstz5see",
                "ruhama" => "https://matermost.finosys-sbs.com/hooks/7kgfirk1sbrepxibtag5otsiwe",
                "finosys" => "https://matermost.finosys-sbs.com/hooks/u8okeire1pnofr3p1kfihr934w",
                
                _ => null
            };
            //var result = await _httpClient.PostAsJsonAsync("https://matermost.finosys-sbs.com/hooks/wtbmppdtqfgydmymzrxstz5see", payload); //Team SINA
            var result = await _httpClient.PostAsJsonAsync(webhookUrl, payload);
            if (result == null)
            {
                return;
            }

        }
        //private async Task SendToMattermost(string teamName, string channel, string message)
        //{
        //    string webhookUrl = teamName switch
        //    {
        //        "Finosys" => "http://matermost.finosys-sbs.com/hooks/u8okeire1pnofr3p1kfihr934w",
        //        "SINA" => "https://matermost.finosys-sbs.com/hooks/wtbmppdtqfgydmymzrxstz5see",
        //        "Ruhama" => "https://matermost.finosys-sbs.com/hooks/7x8j5g3f8h4y6q9z1k5j7r8t9a",
        //        _ => null // default case if team not recognized
        //    };

        //    if (webhookUrl == null)
        //    {
        //        // Optionally log error or throw exception
        //        throw new Exception($"No webhook URL configured for team: {teamName}");
        //    }

        //    var payload = new
        //    {
        //        channel = channel,
        //        username = "TicketBot",
        //        text = message
        //    };

        //    var result = await _httpClient.PostAsJsonAsync(webhookUrl, payload);
        //    if (result == null)
        //    {
        //        return;
        //    }
        //}


        private async Task<string> GenerateNextTicketNo()
        {
            var lastTicketWithNo = await _context.Tickets
                .Where(t => t.TicketNo != null)
                .OrderByDescending(t => t.Id)
                .FirstOrDefaultAsync();

            if (lastTicketWithNo == null)
                return "TIC-001";

            var parts = lastTicketWithNo.TicketNo.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[1], out int number))
            {
                return $"TIC-{(number + 1).ToString("D3")}";
            }

            return "TIC-001"; // fallback
        }

        [HttpGet("all")]
        public async Task<IActionResult> GetAllTickets()
        {
            var tickets = await _context.Tickets.ToListAsync();

            var response = tickets.Select(t => new TicketResponseDTO
            {
                Id = t.Id,
                TicketNo = t.TicketNo,
                Topic = t.Topic,
                Detail = t.Detail,
                Severity = t.Severity,
                Location = t.Location,
                ChannelName = t.ChannelName,
                Status = t.Status
            }).OrderByDescending(x=>x.Id);

            return Ok(response);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetTicketById(int id)
        {
            var ticket = await _context.Tickets.FindAsync(id);

            if (ticket == null)
                return NotFound("Ticket not found.");

            var response = new TicketResponseDTO
            {
                Id = ticket.Id,
                TicketNo = ticket.TicketNo,
                Topic = ticket.Topic,
                Detail = ticket.Detail,
                Severity = ticket.Severity,
                Location = ticket.Location,
                ChannelName = ticket.ChannelName

            };

            return Ok(response);
        }



        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] MattermostMessageDto dto)
        {
            var payload = new
            {
                channel = dto.Channel,
                //username = dto.Username,
                text = dto.Message
            };

            var response = await _httpClient.PostAsJsonAsync("http://matermost.finosys-sbs.com/hooks/u8okeire1pnofr3p1kfihr934w", payload);

            if (response.IsSuccessStatusCode)
                return Ok("Message sent to Mattermost");

            return StatusCode((int)response.StatusCode, "Failed to send message");
        }

        [HttpPost("receive")]
        public async Task<IActionResult> ReceiveMessage([FromBody] OutgoingMattermostPayload payload)
        {
            // Token validation
            if (payload.Token != "9pf1aqmsmtfobyjo43wnrwhm9a")
            {
                return Unauthorized("Invalid token");
            }

            // Check for the trigger word
            if (payload.Text.StartsWith("testing", StringComparison.OrdinalIgnoreCase))
            {
                var subject = $"Mattermost Trigger: {payload.TriggerWord}";
                var body = $"Message: {payload.Text}\nUser: {payload.UserName}\nChannel: {payload.ChannelName}\nPost ID: {payload.PostId}";

                await SendEmail("huzaifaaadil@gmail.com", subject, body);
                return Ok(); // Respond with 200 to acknowledge Mattermost
            }


            return Ok(); // Still return 200 even if trigger doesn't match
        }

        private async Task SendEmail(string toEmail, string subject, string body)
        {
            using var smtp = new SmtpClient
            {
                Host = _smtpSettings.Host,
                Port = _smtpSettings.Port,
                EnableSsl = _smtpSettings.EnableSsl,
                Credentials = new NetworkCredential(_smtpSettings.Username, _smtpSettings.Password)
            };

            var mail = new MailMessage(_smtpSettings.Username, toEmail, subject, body);
            await smtp.SendMailAsync(mail);
        }

        [HttpPost("senduser")]
        public async Task<IActionResult> SendMessageAsUser([FromBody] MattermostRestMessageDto dto)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "http://matermost.finosys-sbs.com/api/v4/posts");

            // Log token to debug if it's being passed correctly (in a real-world scenario, avoid logging sensitive data)
            Console.WriteLine($"Sending message with Token: {dto.Token}");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", dto.Token);

            var body = new
            {
                channel_id = dto.ChannelId,
                message = dto.Message
            };

            // Log the body to ensure the content is correctly formatted
            var jsonBody = System.Text.Json.JsonSerializer.Serialize(body);
            Console.WriteLine($"Sending message body: {jsonBody}");

            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return Ok("Message sent as real user to Mattermost");
            }

            // Read the error content for debugging
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Error response: {error}");
            return StatusCode((int)response.StatusCode, $"Failed to send message: {error}");
        }

        [HttpGet("getChannelId")]
        public async Task<IActionResult> GetChannelDetails(string teamName, string channelName, string token)
        {
            var url = $"http://matermost.finosys-sbs.com/api/v4/teams/name/{teamName}/channels/name/{channelName}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, $"Failed to fetch channel ID: {error}");
            }

            var content = await response.Content.ReadAsStringAsync();

            // Deserialize JSON response into ChannelDetails object
            var channelDetails = System.Text.Json.JsonSerializer.Deserialize<ChannelDetails>(content);

            return Ok(channelDetails); // Return the deserialized object
        }

        [HttpGet("getMessagesByDate")]
        public async Task<IActionResult> GetMessagesByDate(
      string channelId,
      string fromDate, // format: "dd-MM-yyyy"
      string token)
        {
            if (!DateTime.TryParseExact(fromDate, "dd-MM-yyyy", null, System.Globalization.DateTimeStyles.None, out var fromDateTime))
                return BadRequest("Invalid fromDate format. Use dd-MM-yyyy");

            long since = new DateTimeOffset(fromDateTime).ToUnixTimeMilliseconds();
            var url = $"http://matermost.finosys-sbs.com/api/v4/channels/{channelId}/posts?since={since}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, $"Failed to fetch messages: {error}");
            }

            var stream = await response.Content.ReadAsStreamAsync();
            var result = await System.Text.Json.JsonSerializer.DeserializeAsync<MattermostPostsResponse>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return Ok(result);
        }


    }
    public class MattermostCloseResponse
    {
        [FromForm(Name = "channel_id")]
        public string ChannelId { get; set; }

        [FromForm(Name = "user_id")]
        public string UserId { get; set; }

        [FromForm(Name = "text")]
        public string Text { get; set; }

        [FromForm(Name = "team_id")]
        public string TeamId { get; set; }

        [FromForm(Name = "command")]
        public string Command { get; set; }

        [FromForm(Name = "token")]
        public string Token { get; set; }

        [FromForm(Name = "trigger_id")]
        public string TriggerId { get; set; }

        [FromForm(Name = "response_url")]
        public string ResponseUrl { get; set; }

        [FromForm(Name = "user_name")]
        public string UserName { get; set; }
    }

    public class MattermostPostsResponse
    {
        [JsonPropertyName("order")]
        public List<string> Order { get; set; } // List of post IDs in the order they were posted

        [JsonPropertyName("posts")]
        public Dictionary<string, MattermostPost> Posts { get; set; } // Dictionary of post IDs to post details
    }

    public class MattermostPost
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("create_at")]
        public long CreateAt { get; set; }

        [JsonPropertyName("update_at")]
        public long UpdateAt { get; set; }

        [JsonPropertyName("user_id")]
        public string UserId { get; set; }

        [JsonPropertyName("channel_id")]
        public string ChannelId { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        // Add other fields as necessary
    }

    public class ChannelDetails
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("team_id")]
        public string TeamId { get; set; }

        [JsonPropertyName("create_at")]
        public long CreateAt { get; set; }

        [JsonPropertyName("update_at")]
        public long UpdateAt { get; set; }

        [JsonPropertyName("delete_at")]
        public long DeleteAt { get; set; }

        [JsonPropertyName("purpose")]
        public string Purpose { get; set; }

        [JsonPropertyName("header")]
        public string Header { get; set; }

        [JsonPropertyName("last_post_at")]
        public long LastPostAt { get; set; }

        [JsonPropertyName("total_msg_count")]
        public int TotalMsgCount { get; set; }
    }

    public class MattermostMessageDto
    {
        public string MyProperty { get; set; }
        public string Message { get; set; }
        public string Channel { get; set; }
        public string Username { get; set; }
    }

    public class MattermostRestMessageDto
    {
        public string Message { get; set; }
        public string ChannelId { get; set; } // Use Channel ID instead of name
        public string Token { get; set; }
    }
    public class SmtpSettings
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public bool EnableSsl { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }
    public class OutgoingMattermostPayload
    {
        [JsonPropertyName("token")]
        public string Token { get; set; }

        [JsonPropertyName("team_id")]
        public string TeamId { get; set; }

        [JsonPropertyName("team_domain")]
        public string TeamDomain { get; set; }

        [JsonPropertyName("channel_id")]
        public string ChannelId { get; set; }

        [JsonPropertyName("channel_name")]
        public string ChannelName { get; set; }

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }  // Keep as long since it's a Unix timestamp

        [JsonPropertyName("user_id")]
        public string UserId { get; set; }

        [JsonPropertyName("user_name")]
        public string UserName { get; set; }

        [JsonPropertyName("post_id")]
        public string? PostId { get; set; } = null;

        [JsonPropertyName("text")]
        public string Text { get; set; }

        [JsonPropertyName("trigger_word")]
        public string TriggerWord { get; set; }
        [JsonPropertyName("file_ids")]
        public string file_ids { get; set; }
    }
}
