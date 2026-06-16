using SatisfactorySaveParser;
using SatisfactorySaveParser.Structures;

namespace SatisfactorySaveEditor.Model
{
    public class SaveEntityModel : SaveObjectModel
    {
        private bool needTransform;
        private bool wasPlacedInLevel;
        private Vector4 rotation;
        private Vector3 position;
        private Vector3 scale;
        private string parentObjectRoot;
        private string parentObjectName;

        public bool NeedTransform
        { 
            get => needTransform;
            set { SetProperty(ref needTransform, value, nameof(NeedTransform)); }
        }

        public bool WasPlacedInLevel
        {
            get => wasPlacedInLevel;
            set { SetProperty(ref wasPlacedInLevel, value, nameof(WasPlacedInLevel)); }
        }

        public Vector4 Rotation
        {
            get => rotation;
            set { SetProperty(ref rotation, value, nameof(Rotation)); }
        }
        public Vector3 Position
        {
            get => position;
            set { SetProperty(ref position, value, nameof(Position)); }
        }

        public Vector3 Scale
        {
            get => scale;
            set { SetProperty(ref scale, value, nameof(Scale)); }
        }

        public string ParentObjectRoot
        {
            get => parentObjectRoot;
            set { SetProperty(ref parentObjectRoot, value, nameof(ParentObjectRoot)); }
        }

        public string ParentObjectName
        {
            get => parentObjectName;
            set { SetProperty(ref parentObjectName, value, nameof(ParentObjectName)); }
        }

        public SaveEntityModel(SaveEntity ent) : base(ent)
        {
            NeedTransform = ent.NeedTransform;
            WasPlacedInLevel = ent.WasPlacedInLevel;
            ParentObjectRoot = ent.ParentObjectRoot;
            ParentObjectName = ent.ParentObjectName;

            Rotation = ent.Rotation;
            Position = ent.Position;
            Scale = ent.Scale;
        }

        public override void ApplyChanges()
        {
            base.ApplyChanges();

            var model = (SaveEntity) Model;

            model.NeedTransform = NeedTransform;
            model.Rotation = Rotation;
            model.Position = Position;
            model.Scale = Scale;
            model.WasPlacedInLevel = WasPlacedInLevel;
            model.ParentObjectRoot = ParentObjectRoot;
            model.ParentObjectName = ParentObjectName;
        }
    }
}
