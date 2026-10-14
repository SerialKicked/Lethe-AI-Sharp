using LetheAISharp.LLM;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Text;

namespace LetheAISharp.Agent.Tools
{
    /// <summary>
    /// Example toolset demonstrating tool responses that carry images.
    /// Tools keep their standard <c>Task&lt;string&gt;</c> signature and embed a media payload in
    /// their JSON result via <see cref="ToolMediaResult.BuildResult"/>; the engine then unpacks it
    /// and attaches the image to the tool result message exactly like an image attached to a
    /// user message, so a vision-capable backend lets the LLM "see" what the tool returned.
    /// </summary>
    public class MediaTools : IToolList
    {
        public string Id => "Media";

        public string Description => "A set of tools for viewing image files.";

        public string SystemPromptInstruction => "You have access to [Media] tools that let you view image files. Use them when the user asks you to look at, describe or analyze an image. The image is attached to the tool result, so you can see and describe it directly.";

        private List<Tool> toolList = [];

        public IReadOnlyList<Tool> GetToolList() => toolList;

        public void LoadTools(bool clearExisting = false)
        {
            toolList.Clear();
            if (clearExisting)
            {
                Tool.ClearRegisteredTools();
            }
            toolList.Add(Tool.GetOrCreateTool(this, nameof(ViewImage), "[Media] Use this tool to view an image file or an http/https image URL. The image is attached to the result so you can see and describe it. Provide the full path to the image."));
        }

        public void UnloadTools()
        {
            foreach (var tool in toolList)
            {
                Tool.TryUnregisterTool(tool);
            }
            toolList.Clear();
        }

        /// <summary>
        /// Loads an image from a local path (or an http/https URL) and attaches it to the tool
        /// result so the LLM can see it, provided a vision-capable backend is loaded.
        /// </summary>
        /// <remarks>
        /// Demonstrates the media-attachment API: the tool returns a JSON result built with
        /// <see cref="ToolMediaResult.BuildResult"/>, embedding the image path under the
        /// <c>__media</c> key. The engine unpacks the payload, converts the image (resize +
        /// PNG re-encode, same pipeline as user-message images) and attaches it to the tool
        /// response message as image content parts. A payload with no usable image degrades
        /// to a plain text result, so the tool needs no special error paths for that.
        /// </remarks>
        /// <param name="path"> Full path to the image file, or an http/https URL to an image. </param>
        /// <returns> A JSON result describing the outcome, with the image embedded when it could be loaded. </returns>
        public Task<string> ViewImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return Task.FromResult(ToolMediaResult.BuildResult("Error: no image path was provided.", "Media", "media"));

            // Remote URLs are forwarded untouched by the engine (the backend fetches them itself);
            // local paths are validated here so the LLM gets actionable feedback instead of a
            // silently dropped image.
            if (!ToolMediaResult.IsRemote(path) && !File.Exists(path))
                return Task.FromResult(ToolMediaResult.BuildResult($"Error: no image found at '{path}'.", "Media", "media"));

            var result = new StringBuilder();
            result.AppendLinuxLine($"Image loaded from '{path}'.");
            if (!LLMEngine.SupportsVision)
                result.AppendLinuxLine("Note: no vision-capable model is currently loaded, so the image was not attached; you can only reference it by path.");
            return Task.FromResult(ToolMediaResult.BuildResult(result.ToString(), "Media", "media", path));
        }
    }
}
