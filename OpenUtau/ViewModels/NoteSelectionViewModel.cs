using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.ViewModels {
    public class NoteSelectionViewModel: IEnumerable<UNote> {
        public NoteSelectionViewModel() {}


        /// <summary>
        /// Actual selected notes. Stored as sorted set to always have correct first/last
        /// </summary>
        private readonly SortedSet<UNote> _notes = new SortedSet<UNote>();
        /// <summary>
        /// true if the range is being resized from left side of selection rather than right
        /// </summary>
        private bool IsReversed = false;

        /// <summary>
        /// Temporary set for in-progress range selection
        /// </summary>
        public readonly HashSet<UNote> TempSelectedNotes = new HashSet<UNote>();
        /// <summary>
        /// The selection move/change point (either first/last of selection)
        /// </summary>
        public UNote? Head => IsReversed ? FirstOrDefault() : LastOrDefault();
        
        public int Count => _notes.Count;
        public bool IsEmpty => _notes.Count == 0;
        private bool IsMultiple => _notes.Count > 1;
        public UNote? FirstOrDefault() { return _notes.FirstOrDefault(); }
        public UNote? LastOrDefault() { return _notes.LastOrDefault(); }
        
        /// <summary>
        /// Add note to selection
        /// </summary>
        /// <param name="note"></param>
        /// <returns></returns>
        public bool Add(UNote note) {
            var wasAdded = _notes.Add(note);
            return wasAdded;
        }
        public bool Add(IEnumerable<UNote> notes) {
            bool wasChange = false;
            lock (_notes) {
                var initialFirst = _notes.FirstOrDefault();
                foreach (var note in notes) {
                    if (_notes.Add(note)) {
                        wasChange = true;
                    }
                }
                if (wasChange) {
                    // check if added were put in the front or in the back
                    // NOTE this relies on Notes being sortedlist
                    IsReversed = initialFirst != FirstOrDefault();
                }
            }
            return wasChange;
        }
        public bool Remove(UNote note) {
            var wasRemoved = _notes.Remove(note);
            // reset IsReversed if collapsed to single/empty
            if (wasRemoved && Count <= 1) {
                IsReversed = false;
            }
            return wasRemoved;
        }
        public bool Remove(IEnumerable<UNote> notes) {
            bool wasChange = false;
            lock (_notes) {
                foreach (var note in notes) {
                    wasChange |= _notes.Remove(note);
                }
            }
            // reset IsReversed if collapsed to single/empty
            if (wasChange && Count <= 1) {
                IsReversed = false;
            }
            return wasChange;
        }
        /// <summary>
        /// Swap out current selection with single note
        /// </summary>
        /// <param name="note"></param>
        public bool Select(UNote? note) {
            if (_notes.Count == 1 && Head == note) {
                return false;
            }
            lock (_notes) {
                SelectNone();
                if (note != null) {
                    _notes.Add(note);
                }
            }
            return true;
        }
        public bool Select(UVoicePart part) {
            return Select(part.notes);
        }
        public bool Select(IEnumerable<UNote> notes) {
            lock (_notes) {
                SelectNone();
                foreach (var note in notes) {
                    _notes.Add(note);
                }
            }
            return true;
        }
        public bool Select(UNote start, UNote end) {
            // NOTE edge case where start and end are at the same exact position but added out of order
            // but i think that's unlikely
            // ensure in positive direction
            if (start.position > end.position) {
                var tmp = start;
                start = end;
                end = tmp;
            }
            var cursor = start;
            lock (_notes) {
                SelectNone();
                do {
                    _notes.Add(cursor);
                    cursor = cursor.Next;
                } while (cursor != end && cursor?.Next != null);
            }
            return true;
        }
        public bool SelectAll() {
            int initialCount = _notes.Count;
            IsReversed = false;
            TempSelectedNotes.Clear();
            lock (_notes) {
                var cursor = _notes.FirstOrDefault();
                while ((cursor = cursor?.Prev) != null) {
                    _notes.Add(cursor);
                }
                cursor = _notes.LastOrDefault();
                while ((cursor = cursor?.Next) != null) {
                    _notes.Add(cursor);
                }
                return _notes.Count != initialCount;
            }
        }
        public bool SelectNone() {
            var ret = IsEmpty ? false : true;
            TempSelectedNotes.Clear();
            _notes.Clear();
            IsReversed = false;
            return ret;
        }
        public bool SelectToStart() {
            TempSelectedNotes.Clear();
            // always moves head to front
            IsReversed = true;
            bool wasChange = false;
            lock (_notes) {
                var cursor = _notes.FirstOrDefault();
                while ((cursor = cursor?.Prev) != null) {
                    wasChange |= _notes.Add(cursor);
                }
            }
            return wasChange;
        }
        public bool SelectToEnd() {
            TempSelectedNotes.Clear();
            // always moves head to back
            IsReversed = false;
            bool wasChange = false;
            lock (_notes) {
                var cursor = _notes.LastOrDefault();
                while ((cursor = cursor?.Next) != null) {
                    wasChange |= _notes.Add(cursor);
                }
            }
            return wasChange;
        }
        public bool SelectTo(UNote note) {
            if (note == null) {
                return false;
            }
            TempSelectedNotes.Clear();
            // if empty selection just select specified note
            if (IsEmpty) {
                return Select(note);
            }
            bool wasChange = false;
            lock (_notes) {
                IsReversed = note.position <= _notes.First().position;
                var cursor = IsReversed ? _notes.FirstOrDefault() : _notes.LastOrDefault();
                while ((cursor = IsReversed ? cursor?.Prev : cursor?.Next) != null) {
                    wasChange |= _notes.Add(cursor);
                    if (cursor == note) {
                        break;
                    }
                }
            }
            return wasChange;
        }
        /// <summary>
        /// Move selection over one, collapsing if multiple selection
        /// </summary>
        /// <param name="delta">1 for Next, -1 for Prev</param>
        /// <returns></returns>
        public bool Move(int delta) {
            if (IsEmpty) {
                return false;
            }
            int movesRemaining = Math.Abs(delta);
            bool isForwardMove = delta > 0;
            // if multiple selection then collapse to first/last item
            if (IsMultiple) {
                return Select(isForwardMove ? _notes.Last() : _notes.First());
            }
            bool wasChange = false;
            lock (_notes) {
                UNote? cursor = IsReversed ? _notes.First() : _notes.Last();
                while (
                    // reduce # of moves, check if done
                    movesRemaining-- > 0 &&
                    // move to next/prev node and make sure can keep going
                    (cursor = isForwardMove ? cursor?.Next : cursor?.Prev) != null
                ) {
                    wasChange = true;
                }

                // clear out current selection and select head (collapses multiple selection)
                if (wasChange) {
                    Select(cursor);
                    return true;
                }
            }
            return false;
        }
        /// <summary>
        /// Expand/shrink selection in specified delta direction
        /// </summary>
        /// <param name="delta">Move selection "head" left (-1) or right (1)</param>
        /// <returns></returns>
        public bool Resize(int delta) {
            if (IsEmpty || delta == 0) {
                return false;
            }
            int movesRemaining = Math.Abs(delta);
            bool isForwardMove = delta > 0;
            bool wasChange = false;

            lock (_notes) {
                UNote head = IsReversed ? _notes.First() : _notes.Last();
                UNote? cursor = head;
                while (
                    // reduce # of moves, check if done
                    movesRemaining-- > 0 &&
                    // move to next/prev node and make sure can keep going
                    (cursor = isForwardMove ? cursor?.Next : cursor?.Prev) != null
                ) {
                    // if single selection then update direction based on delta
                    if (_notes.Count == 1) {
                        IsReversed = !isForwardMove;
                    }
                    
                    // shrink selection if move is towards center
                    bool isShrink = IsReversed ? isForwardMove : !isForwardMove;

                    if (isShrink) {
                        wasChange |= _notes.Remove(head);
                    } else {
                        wasChange |= _notes.Add(cursor);
                    }
                    head = cursor;
                }
            }

            return wasChange;
        }
        public bool MovePrev() {
            return Move(-1);
        }
        public bool MoveNext() {
            return Move(1);
        }
        public bool Expand(int delta = 1) {
            return Resize(IsReversed ? -delta : delta);
        }
        public bool Shrink(int delta = 1) {
            if (_notes.Count - delta <= 0) {
                return Select(Head);
            }
            return Resize(IsReversed ? delta : -delta);
        }
        /// <summary>
        /// If multiple selection shrink to 1, otherwise clear
        /// </summary>
        /// <returns></returns>
        public bool Cancel() {
            switch (_notes.Count) {
                case 0:
                    return false;
                case 1:
                    SelectNone();
                    return true;
                default:
                    lock (_notes) {
                        var singleNote = Head ?? _notes.First();
                        Select(singleNote);
                    }
                    return true;
            }
        }
        public void SetTemporarySelection(IEnumerable<UNote> notes) {
            TempSelectedNotes.Clear();
            lock (TempSelectedNotes) {
                foreach (var note in notes) {
                    TempSelectedNotes.Add(note);
                }
            }
        }
        public void CommitTemporarySelection() {
            Add(TempSelectedNotes);
            TempSelectedNotes.Clear();
        }
        public List<UNote> ToList() {
            return _notes.ToList();
        }
        public UNote[] ToArray() {
            return _notes.ToArray();
        }
        public IEnumerator<UNote> GetEnumerator() {
            return _notes.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return ((IEnumerable)_notes).GetEnumerator();
        }
    }
}
