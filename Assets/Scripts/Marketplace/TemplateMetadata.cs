using System;
using UnityEngine;

namespace FluidWorks.Marketplace
{
    [Serializable]
    public class TemplateMetadata
    {
        public string id;
        public string name;
        public string author;
        public string category;
        public string solver;
        public string difficulty;
        public int ReynoldsNumber;
        public string version;
        
        // Marketplace specific fields
        public string previewUrl;
        public string downloadUrl;
        public long fileSize; // in bytes
        
        // UI Display Helpers
        public string DisplaySize => (fileSize / 1024f / 1024f).ToString("F1") + " MB";
    }

    [Serializable]
    public class MarketplaceResponse
    {
        public TemplateMetadata[] templates;
    }
}
