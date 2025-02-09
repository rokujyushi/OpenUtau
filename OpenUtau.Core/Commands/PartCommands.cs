using System.Linq;
using System;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Format.MusicXMLSchema;

namespace OpenUtau.Core {
    public abstract class PartCommand : UCommand {
        public readonly UProject project;
        public readonly UPart part;
        public override ValidateOptions ValidateOptions => new ValidateOptions {
            SkipTiming = true,
        };
        public PartCommand(UProject project, UPart part) {
            this.project = project;
            this.part = part;
        }
    }

    public class AddPartCommand : PartCommand {
        public AddPartCommand(UProject project, UPart part) : base(project, part) { }
        public override string ToString() => "Add part";
        public override void Execute() => project.parts.Add(part);
        public override void Unexecute() => project.parts.Remove(part);
    }

    public class RemovePartCommand : PartCommand {
        public RemovePartCommand(UProject project, UPart part) : base(project, part) { }
        public override string ToString() => "Remove parts";
        public override void Execute() => project.parts.Remove(part);
        public override void Unexecute() => project.parts.Add(part);
    }

    public class MovePartCommand : PartCommand {
        public readonly int newPos;
        public readonly int oldPos;
        public readonly int newTrackNo;
        public readonly int oldTrackNo;
        public MovePartCommand(UProject project, UPart part, int position, int trackNo) : base(project, part) {
            newPos = position;
            newTrackNo = trackNo;
            oldPos = part.position;
            oldTrackNo = part.trackNo;
        }
        public override string ToString() => "Move parts";
        public override void Execute() {
            part.position = newPos;
            part.trackNo = newTrackNo;
        }
        public override void Unexecute() {
            part.position = oldPos;
            part.trackNo = oldTrackNo;
        }
    }

    public class ResizePartCommand : PartCommand {
        readonly int newDur, oldDur;
        public ResizePartCommand(UProject project, UPart part, int duration) : base(project, part) {
            newDur = duration;
            oldDur = part.Duration;
        }
        public override string ToString() => "Change parts duration";
        public override void Execute() => part.Duration = newDur;
        public override void Unexecute() => part.Duration = oldDur;
    }

    public class RenamePartCommand : PartCommand {
        readonly string newName, oldName;
        public RenamePartCommand(UProject project, UPart part, string name) : base(project, part) {
            newName = name;
            oldName = part.name;
        }
        public override string ToString() => "Rename part";
        public override void Execute() => part.name = newName;
        public override void Unexecute() => part.name = oldName;
    }

    public class ReplacePartCommand : PartCommand {
        public readonly int index;
        public readonly UPart newPart;
        public ReplacePartCommand(UProject project, UPart part, UPart newPart) : base(project, part) {
            index = project.parts.IndexOf(part);
            this.newPart = newPart;
        }
        public override string ToString() => "Replace part";
        public override void Execute() => project.parts[index] = newPart;
        public override void Unexecute() => project.parts[index] = part;
    }

    public class AddBookMarkCommand : PartCommand {
        protected UVoicePart part;
        protected int tick;
        protected string text;
        protected int index;
        public AddBookMarkCommand(UProject project, UVoicePart part, int tick, string text, int index) : base(project, part) {
            this.part = part;
            this.tick = tick;
            this.text = text;
            this.index = index;
        }
        protected AddBookMarkCommand(UProject project, UPart part) : base(project, part) { }
        public override void Execute() {
            int index = part.bookmarks.FindIndex(bookmark => bookmark.position == tick);
            var bookMark = new UBookmark {
                position = tick,
                text = text,
                index = part.bookmarks.Count() + 1
            };
            if (index >= 0) {
                part.bookmarks.Insert(index - 1, bookMark);
            } else {
                part.bookmarks.Add(bookMark);
            }
        }
        public override void Unexecute() {
            int index = part.bookmarks.FindIndex(bookmark => bookmark.position == tick);
            if (index >= 0) {
                part.bookmarks.RemoveAt(index);
            } else {
                throw new Exception("Cannot remove non-exist time signature change");
            }
        }
        public override string ToString() => $"Add bookmark change {part.name}_{index}:{text} at position {tick}";
    }

    public class DelBookMarkCommand : AddBookMarkCommand {
        public DelBookMarkCommand(UProject project, UPart part, int tick) : base(project, part) {
            if(part == null) {
                return;
            }
            this.part = (part as UVoicePart);
            this.tick = tick;
            var bookMark = this.part.bookmarks.Find(bookmark => bookmark.position >= tick);
            text = bookMark.text;
            index = bookMark.index;
        }
        public override void Execute() {
            base.Unexecute();
        }
        public override void Unexecute() {
            base.Execute();
        }
        public override string ToString() => $"Del bookmark change {part.name}_{index}:{text} at position {tick}";
    }

}
