using System.Xml.Serialization;

// ====================================
// SalarDbCodeGenerator
// http://SalarDbCodeGenerator.codeplex.com
// Salar Khalilzadeh <salar2k@gmail.com>
// © 2012, All rights reserved
// ====================================
namespace SalarDbCodeGenerator.Schema.Patterns
{
    /// <summary>
    /// The "Content" key with in "PatternContent" tag
    /// </summary>
    public class ConditionItem
    {
        public ConditionItem()
        {
            Key = string.Empty;
            ContentText = string.Empty;
        }

        /// <summary>
        /// Replacement mode
        /// </summary>
        [XmlAttribute]
        public string Key { get; set; }

        /// <summary>
        /// Replacement Content
        /// </summary>
        [XmlText]
        public string ContentText { get; set; }

        [XmlAttribute]
        public string MatchValue { get; set; }

        public override string ToString()
        {
            return Key;
        }
    }
}