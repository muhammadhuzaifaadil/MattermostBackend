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

namespace MattermostBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MattermostController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly SmtpSettings _smtpSettings;
        private const string MattermostToken = "gokcroshrp8pprgasm7pqj7jsc"; // Replace with your actual token

        public MattermostController(AppDbContext context,HttpClient httpClient, IOptions<SmtpSettings> smtpSettings)
        {
            _context = context;
            _httpClient = httpClient;
            _smtpSettings = smtpSettings.Value;

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
                ChannelName = dto.ChannelName
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

            // Send to Mattermost
            await SendToMattermost(ticket.ChannelName, message);

            return Ok(new { message = "Ticket created", ticketId = ticket.Id, ticketNo = ticket.TicketNo });
        }
        //[HttpPost("upload")]
        //public async Task<IActionResult> UploadTicket([FromBody] CreateTicketDto dto)
        //{
        //    // Generate the next TicketNo BEFORE inserting
        //    string nextTicketNo = await GenerateNextTicketNo();

        //    var ticket = new Ticket
        //    {
        //        TicketNo = nextTicketNo,
        //        Topic = dto.Topic,
        //        Detail = dto.Detail,
        //        Severity = dto.Severity,
        //        Location = dto.Location,
        //        ChannelName = dto.ChannelName
        //    };

        //    _context.Tickets.Add(ticket);
        //    await _context.SaveChangesAsync();

        //    return Ok(new { message = "Ticket created", ticketId = ticket.Id, ticketNo = ticket.TicketNo });
        //}

        private async Task SendToMattermost(string Channel, string message)
        {
            var payload = new
            {
                channel = Channel,
                username = "TicketBot",
                text = message
            };

           var result =  await _httpClient.PostAsJsonAsync("http://matermost.finosys-sbs.com/hooks/u8okeire1pnofr3p1kfihr934w", payload);
            if(result == null)
            {
                return;
            }

        }

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
                ChannelName = t.ChannelName
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
            var jsonBody = JsonSerializer.Serialize(body);
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
            var channelDetails = JsonSerializer.Deserialize<ChannelDetails>(content);

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
            var result = await JsonSerializer.DeserializeAsync<MattermostPostsResponse>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return Ok(result);
        }


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
