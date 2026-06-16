using SatisfactorySaveParser.Save;

namespace SatisfactorySaveEditor.Model
{
    public class SaveRootModel : SaveObjectModel
    {
        private readonly FSaveHeader model;

        private int buildVersion;
        private string mapName;
        private string mapOptions;
        private string sessionName;
        private int playDuration;
        private long saveDateTime;
        private ESessionVisibility sessionVisibility;

        public SaveHeaderVersion HeaderVersion => model.HeaderVersion;

        public FSaveCustomVersion SaveVersion => model.SaveVersion;

        public int BuildVersion
        {
            get => buildVersion;
            set { SetProperty(ref buildVersion, value, nameof(BuildVersion)); }
        }

        public string MapName
        {
            get => mapName;
            set { SetProperty(ref mapName, value, nameof(MapName)); }
        }

        public string MapOptions
        {
            get => mapOptions;
            set { SetProperty(ref mapOptions, value, nameof(MapOptions)); }
        }

        public string SessionName
        {
            get => sessionName;
            set { SetProperty(ref sessionName, value, nameof(SessionName)); }
        }

        public int PlayDuration
        {
            get => playDuration;
            set { SetProperty(ref playDuration, value, nameof(PlayDuration)); }
        }

        public bool HasSessionVisibility => HeaderVersion >= SaveHeaderVersion.AddedSessionVisibility;

        public ESessionVisibility SessionVisibility
        {
            get => sessionVisibility;
            set { SetProperty(ref sessionVisibility, value, nameof(SessionVisibility)); }
        }

        public long SaveDateTime
        {
            get => saveDateTime;
            set { SetProperty(ref saveDateTime, value, nameof(SaveDateTime)); }
        }

        public SaveRootModel(FSaveHeader header) : base(header.SessionName)
        {
            model = header;
            Type = "Root";

            buildVersion = model.BuildVersion;
            mapName = model.MapName;
            mapOptions = model.MapOptions;
            sessionName = model.SessionName;
            playDuration = model.PlayDuration;
            sessionVisibility = model.SessionVisibility;
            saveDateTime = model.SaveDateTime;
        }

        public override void ApplyChanges()
        {
            base.ApplyChanges();

            model.BuildVersion = buildVersion;
            model.MapName = mapName;
            model.MapOptions = mapOptions;
            model.SessionName = sessionName;
            model.PlayDuration = playDuration;
            model.SessionVisibility = sessionVisibility;
            model.SaveDateTime = saveDateTime;
        }
    }
}
