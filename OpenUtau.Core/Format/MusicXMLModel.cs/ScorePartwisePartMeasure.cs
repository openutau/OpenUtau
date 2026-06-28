using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace OpenUtau.Core.Format.MusicXMLSchema {
    public interface IMeasureElement { }

    public partial class ScorePartwisePartMeasure : IMeasureAttributes, IXmlSerializable {

        /// <summary>
        /// <para xml:lang="en">Initializes a new instance of the <see cref="ScorePartwisePartMeasure" /> class.</para>
        /// </summary>
        public ScorePartwisePartMeasure() {}

        [XmlIgnore] public List<IMeasureElement> Content { get; set; } = new List<IMeasureElement>();

        [XmlIgnore] public List<Note> Notes => Content.OfType<Note>().ToList();
        [XmlIgnore] public List<Backup> Backups => Content.OfType<Backup>().ToList();
        [XmlIgnore] public List<Forward> Forwards => Content.OfType<Forward>().ToList();
        [XmlIgnore] public List<Direction> Directions => Content.OfType<Direction>().ToList();
        [XmlIgnore] public List<Attributes> Attributes => Content.OfType<Attributes>().ToList();
        [XmlIgnore] public List<Harmony> Harmonies => Content.OfType<Harmony>().ToList();
        [XmlIgnore] public List<FiguredBass> FiguredBasses => Content.OfType<FiguredBass>().ToList();
        [XmlIgnore] public List<Print> Prints => Content.OfType<Print>().ToList();
        [XmlIgnore] public List<Sound> Sounds => Content.OfType<Sound>().ToList();
        [XmlIgnore] public List<Listening> Listenings => Content.OfType<Listening>().ToList();
        [XmlIgnore] public List<Barline> Barlines => Content.OfType<Barline>().ToList();
        [XmlIgnore] public List<Grouping> Groupings => Content.OfType<Grouping>().ToList();
        [XmlIgnore] public List<Link> Links => Content.OfType<Link>().ToList();
        [XmlIgnore] public List<Bookmark> Bookmarks => Content.OfType<Bookmark>().ToList();


        [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
        [XmlAttribute("number")]
        public string Number { get; set; } = "0";

        /// <summary>
        /// <para>The measure-text type is used for the text attribute of measure elements. It has at least one character. The implicit attribute of the measure element should be set to "yes" rather than setting the text attribute to an empty string.</para>
        /// <para xml:lang="en">Minimum length: 1.</para>
        /// </summary>
        [System.ComponentModel.DataAnnotations.MinLength(1)]
        [XmlAttribute("text")]
        public string Text { get; set; }

        [XmlAttribute("implicit")]
        public YesNo Implicit { get; set; } = YesNo.No;

        /// <summary>
        /// <para xml:lang="en">Gets or sets a value indicating whether the Implicit property is specified.</para>
        /// </summary>
        [XmlIgnore()]
        public bool ImplicitSpecified { get; set; }

        [XmlAttribute("non-controlling")]
        public YesNo NonControlling { get; set; }

        /// <summary>
        /// <para xml:lang="en">Gets or sets a value indicating whether the NonControlling property is specified.</para>
        /// </summary>
        [XmlIgnore()]
        public bool NonControllingSpecified { get; set; }

        /// <summary>
        /// <para>The tenths type is a number representing tenths of interline staff space (positive or negative). Both integer and decimal values are allowed, such as 5 for a half space and 2.5 for a quarter space. Interline space is measured from the middle of a staff line.
        ///
        ///Distances in a MusicXML file are measured in tenths of staff space. Tenths are then scaled to millimeters within the scaling element, used in the defaults element at the start of a score. Individual staves can apply a scaling factor to adjust staff size. When a MusicXML element or attribute refers to tenths, it means the global tenths defined by the scaling element, not the local tenths as adjusted by the staff-size element.</para>
        /// </summary>
        [XmlAttribute("width")]
        public decimal Width { get; set; }

        /// <summary>
        /// <para xml:lang="en">Gets or sets a value indicating whether the Width property is specified.</para>
        /// </summary>
        [XmlIgnore()]
        public bool WidthSpecified { get; set; }

        [XmlAttribute("id")]
        public string Id { get; set; }

        public System.Xml.Schema.XmlSchema GetSchema() => null;

        public void WriteXml(XmlWriter writer) {
            throw new System.NotImplementedException();
        }

        public void ReadXml(XmlReader reader) {
            // 1. Read and set Measure's own attributes (e.g. "number")
            if (reader.MoveToAttribute("number")) {
                this.Number = reader.Value;
                reader.MoveToElement();
            }
            if (reader.MoveToAttribute("id")) {
                this.Id = reader.Value;
                reader.MoveToElement();
            }
            if (reader.MoveToAttribute("implicit")) {
                if (YesNo.TryParse(reader.Value, out YesNo implicitValue)) {
                    this.Implicit = implicitValue;
                    this.ImplicitSpecified = true;
                }
                reader.MoveToElement();
            }
            if (reader.MoveToAttribute("non-controlling")) {
                if (YesNo.TryParse(reader.Value, out YesNo nonControllingValue)) {
                    this.NonControlling = nonControllingValue;
                    this.NonControllingSpecified = true;
                }
                reader.MoveToElement();
            }
            if (reader.MoveToAttribute("text")) {
                this.Text = reader.Value;
                reader.MoveToElement();
            }
            if (reader.MoveToAttribute("width")) {
                if (decimal.TryParse(reader.Value, out decimal widthValue)) {
                    this.Width = widthValue;
                    this.WidthSpecified = true;
                }
                reader.MoveToElement();
            }

            // 2. Enter the Measure element (read start tag)
            reader.ReadStartElement();

            // 3. Loop to read all child elements inside Measure
            while (reader.NodeType != XmlNodeType.EndElement && reader.NodeType != XmlNodeType.None) {
                if (reader.NodeType == XmlNodeType.Element) {
                    IMeasureElement element = null;
                    string elementName = reader.LocalName;

                    // All possible elements in measure: 
                    // https://www.w3.org/2021/06/musicxml40/musicxml-reference/elements/measure-partwise/
                    switch (elementName) {
                        case "note":
                            element = DeserializeElement<Note>(reader);
                            break;
                        case "backup":
                            element = DeserializeElement<Backup>(reader);
                            break;
                        case "forward":
                            element = DeserializeElement<Forward>(reader);
                            break;
                        case "direction":
                            element = DeserializeElement<Direction>(reader);
                            break;
                        case "attributes":
                            element = DeserializeElement<Attributes>(reader);
                            break;
                        case "harmony":
                            element = DeserializeElement<Harmony>(reader);
                            break;
                        case "figured-bass":
                            element = DeserializeElement<FiguredBass>(reader);
                            break;
                        case "print":
                            element = DeserializeElement<Print>(reader);
                            break;
                        case "sound":
                            element = DeserializeElement<Sound>(reader);
                            break;
                        case "listening":
                            element = DeserializeElement<Listening>(reader);
                            break;
                        case "barline":
                            element = DeserializeElement<Barline>(reader);
                            break;
                        case "grouping":
                            element = DeserializeElement<Grouping>(reader);
                            break;
                        case "link":
                            element = DeserializeElement<Link>(reader);
                            break;
                        case "bookmark":
                            element = DeserializeElement<Bookmark>(reader);
                            break;
                        default:
                            // Skip unknown elements directly
                            reader.Skip();
                            break;
                    }

                    if (element != null) {
                        // Add elements in the order they appear in XML
                        this.Content.Add(element);
                    }
                } else {
                    // Skip non-element nodes (such as whitespace, comments)
                    reader.Read();
                }
            }

            // 4. Read the end tag of Measure element
            reader.ReadEndElement();
        }
        
        private T DeserializeElement<T>(XmlReader reader) where T : IMeasureElement{
            // 使用一个临时的 XmlSerializer 来处理 T 类型的元素
            var serializer = new XmlSerializer(typeof(T));
            
            // Deserialize 方法会自动消耗掉 T 元素的起始标签和结束标签
            // 所以主循环的 reader 会自动定位到下一个元素。
            return (T)serializer.Deserialize(reader);
        }
    }
}
