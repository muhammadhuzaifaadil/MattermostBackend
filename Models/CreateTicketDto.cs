namespace MattermostBackend.Models
{
    public class CreateTicketDto
    {

        //public string TeamName { get; set; }
        public string ChannelName { get; set; }
        public string Topic { get; set; }
        public string Detail { get; set; }
        public string Severity { get; set; }
        public string Location { get; set; }
        public string TeamName { get; set; }
    }

}
