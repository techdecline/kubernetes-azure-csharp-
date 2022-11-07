public class LandingZoneArgs
{
    public LandingZoneArgs(string environment, string location, string application)
    {
        Environment = environment;
        Location = Location;
        Application = application;
        EnvironmentShort = environment.ToLower() switch
        {
            "prod" => "p",
            "dev" => "d",
            "stage" => "s",
            _ => environment.ToLower().Substring(0, 1)
        };
        LocationShort = location.ToLower() switch
        {
            "westeurope" => "weu",
            "germanywestcentral" => "gwc",
            _ => "glo"
        };
    }
    public string Environment { get; set; } = null!;
    public string EnvironmentShort { get; }
    public string Location { get; set; } = null!;
    public string Application { get; set; }
    public string LocationShort { get; }
}