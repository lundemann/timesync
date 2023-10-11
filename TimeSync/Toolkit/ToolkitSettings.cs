using System.Collections.Generic;
using System.Xml.Serialization;

namespace TimeSync.Toolkit
{
    [XmlRoot("toolkitSettings")]
    public class ToolkitSettings
    {
        [XmlElement("url")]
        public string Url { get; set; }

        [XmlElement("timeRegistrationList")]
        public string TimeRegistrationList { get; set; }

        [XmlElement("caseList")]
        public string CaseList { get; set; }

        [XmlElement("caseActiveStateCodes")]
        public string CaseActiveStateCodes { get; set; }

        [XmlArray("caseDefaultValues")]
        [XmlArrayItem("default")]
        public List<DefaultValueMapping> CaseDefaultValues { get; set; }

        [XmlElement("workPackageList")]
        public string WorkPackageList { get; set; }

        [XmlElement("workPackageActiveStateCodes")]
        public string WorkPackageActiveStateCodes { get; set; }

        [XmlArray("workPackageDefaultValues")]
        [XmlArrayItem("default")]
        public List<DefaultValueMapping> WorkPackageDefaultValues { get; set; }

        public class DefaultValueMapping
        {
            [XmlElement("field")]
            public string Field { get; set; }

            [XmlElement("fieldType")]
            public string FieldType { get; set; }

            [XmlElement("value")]
            public string Value { get; set; }
        }
    }
}
