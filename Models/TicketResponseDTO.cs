namespace MattermostBackend.Models
{
    public class TicketResponseDTO
    {
        public int Id { get; set; }
        public string? TicketNo { get; set; }
        public string? ChannelName { get; set; }
        public string? Topic { get; set; }
        public string? Detail { get; set; }
        public string? Severity { get; set; }
        public string? Location { get; set; }
        public bool? Status { get; set; }

    }

}
