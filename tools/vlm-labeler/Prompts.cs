using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace VroidVlmLabeler;

/// <summary>
/// Prompt profiles for the VLM, organised as a tree: each section has its own prompt and a map of
/// per-category prompts. Prompts are self-contained (no project-internal knowledge) and refer only
/// to the visible thumbnail and the section/category names shown in the editor UI.
/// Overridable at runtime via --prompts file.json:
/// {
///   "system": "...",
///   "sections": {
///     "Face": { "prompt": "...", "categories": { "Irises": "..." } },
///     "Outfit": "a plain string is shorthand for the section prompt"
///   }
/// }
/// </summary>
public sealed class PromptBook
{
    private const string DefaultSystem =
        "You are labeling preview thumbnails for a 3D character editor. " +
        "You will see one thumbnail at a time and must give it a short, human-readable English name " +
        "(2-5 words) so a user can recognize it in a list. Describe what is actually visible; " +
        "when unsure, stay general instead of inventing details. " +
        "Reply with ONLY the name: no quotes, no punctuation, no explanation, no trailing period.";

    private const string Fallback =
        "Describe the most visually distinctive feature of the thumbnail as a short English name. " +
        "Prefer shape and style over color alone.";

    private sealed class SectionProfile
    {
        public string Prompt;
        public readonly Dictionary<string, string> Categories = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly Dictionary<string, SectionProfile> _sections = new(StringComparer.OrdinalIgnoreCase);

    public string System { get; private set; } = DefaultSystem;

    public PromptBook(string overrideFile)
    {
        Build();
        if (!string.IsNullOrEmpty(overrideFile)) LoadOverrides(overrideFile);
    }

    public string TaskFor(string section, string category)
    {
        var instruction = Fallback;
        if (_sections.TryGetValue(section, out var profile))
        {
            if (!string.IsNullOrEmpty(category) && profile.Categories.TryGetValue(category, out var c)) instruction = c;
            else if (!string.IsNullOrEmpty(profile.Prompt)) instruction = profile.Prompt;
        }
        return $"This thumbnail is from the section \"{section}\", category \"{category}\". {instruction}";
    }

    private void Build()
    {
        Define("Outfit",
            "This thumbnail shows a clothing item. Name the garment and its most distinctive visible " +
            "trait (shape, cut, style, color, or pattern). " +
            "Examples: White Sailor Uniform, Red Plaid Mini Skirt, Black Leather Jacket, Long Knit Sweater.");

        Define("Body",
            "This thumbnail shows a body or skin option. Describe the silhouette, proportions, or skin " +
            "finish. Examples: Slim Tall, Curvy, Muscular Build, Pale Skin.");

        var face = Define("Face",
            "This thumbnail shows a facial feature or its styling. Describe the SHAPE and styling rather " +
            "than the color, because the color is often shared between different options. " +
            "Examples: Sharp Almond, Round Wide, Thin Arched.");
        face.Categories["Face Sets"] =
            "This is a whole face shape. Describe the overall face shape and impression (round, slim, " +
            "sharp, childlike, mature), not the skin color. Examples: Round Youthful, Slim Mature, Sharp V-Shaped.";
        face.Categories["Eyes Sets"] =
            "This is a whole eye set (lids, lashes and iris together). Describe the eye shape, size, and " +
            "openness, not the color. Examples: Large Round, Narrow Sharp, Droopy Soft.";
        face.Categories["Irises"] =
            "Describe only the SHAPE and PATTERN of the iris, not its color. " +
            "Examples: Radial Starburst, Vertical Gradient, Smooth Solid, Concentric Rings, Two-Tone Split.";
        face.Categories["Eye Highlights"] =
            "Describe the shape and placement of the eye highlight (the bright reflection spot), not its " +
            "color. Examples: Single Small Dot, Twin Highlights, Star Sparkle, No Highlight.";
        face.Categories["Scleras"] =
            "This is the white of the eye; here the difference is the color and shading. Describe the " +
            "sclera as plain, black, colored, or with a gradient/bloodshot pattern. " +
            "Examples: Plain White, Solid Black, Pink Gradient, Bloodshot.";
        face.Categories["Eyebrows"] =
            "Describe the brow shape: thickness, arch, angle, and length, not its color. " +
            "Examples: Thin Arched, Thick Straight, Angled Sharp, Short Soft.";
        face.Categories["Eyelids"] =
            "Describe the eyelid shape: crease type, hooding, and how open the eye looks, not its color. " +
            "Examples: Double Eyelid, Monolid, Hooded, Wide Open.";
        face.Categories["Eyeliner"] =
            "Describe the eyeliner style: thickness, wing shape, and placement, not its color. " +
            "Examples: Thin Natural, Thick Cat Eye, Soft Smudge.";
        face.Categories["Eyelashes"] =
            "Describe the lash style: length, curl, volume, and density, not its color. " +
            "Examples: Long Curled, Short Sparse, Thick Voluminous.";
        face.Categories["Nose"] =
            "Describe the nose shape: bridge width, tip shape, and nostril size. " +
            "Examples: Small Button, Tall Narrow, Wide Flat.";
        face.Categories["Mouth"] =
            "Describe the mouth shape: width, lip curvature, and corner angle, not its color.";
        face.Categories["Mouth Inside"] =
            "Describe the inside of the mouth (teeth and tongue): shape and any distinctive feature such " +
            "as fangs, gaps, or a styled color. Examples: Normal Teeth, Pointed Fangs, Single Tooth.";
        face.Categories["Lips"] =
            "Describe the lip shape: fullness, width, cupid's bow, and contour, not its color. " +
            "Examples: Full Plump, Thin Wide, Small Pouty.";
        face.Categories["Cheeks"] =
            "Describe the shape and placement of the cheek blush, not its color. " +
            "Examples: High Soft Blush, Wide Diffuse, Round Intense.";
        face.Categories["Skin"] =
            "This is a skin option; the difference is the skin tone and finish. Describe the color " +
            "(fair, tan, olive, dark) and the finish (matte or glossy). " +
            "Examples: Fair Cool, Warm Tan, Deep Brown, Glossy.";
        face.Categories["Face Paint"] =
            "Describe the painted design and where it sits on the face (the pattern and shape), not just " +
            "its color. Examples: Star Cheek Marks, Blush Stripes, Freckle Dots.";
        face.Categories["Expression Editor"] =
            "Describe the facial expression shown (happy, angry, surprised, sad, neutral).";

        var hair = Define("Hairstyle",
            "This thumbnail shows a hairstyle. Describe its length, shape, texture, and bangs. " +
            "Prefer the form over the exact color. " +
            "Examples: Long Straight Twin Tails, Short Bob with Bangs, Wavy Medium with Side Part.");
        hair.Categories["Hairstyle Sets"] =
            "This is a complete hairstyle set. Describe the overall length, shape, and style as one look. " +
            "Examples: Long Twin Tails with Bangs, Short Messy Bob, Wavy Ponytail.";
        hair.Categories["Front"] =
            "This is the front hair (bangs/fringe). Describe the bang shape and parting, not the color. " +
            "Examples: Straight Blunt Bangs, Side-Swept Fringe, Curtain Bangs, Full Round Fringe.";
        hair.Categories["Back"] =
            "This is the back hair. Describe its length, volume, and the shape at the back (straight, " +
            "wavy, braided, bun). Examples: Long Straight Back, Short Bob Back, Low Bun.";
        hair.Categories["Overall Hair"] =
            "This is the overall hair; here the color and finish are the difference. Describe the color " +
            "and sheen (glossy or matte). Examples: Glossy Pink, Matte Ash Blonde, Dark Shiny.";
        hair.Categories["Extensions"] =
            "These are extra hair pieces (extensions). Describe their placement, length, and shape. " +
            "Examples: Long Side Extensions, Twin Braids, Curly Extensions.";
        hair.Categories["Side"] =
            "This is the side hair (sidelocks). Describe the length and shape of the side locks. " +
            "Examples: Long Straight Sidelocks, Short Curled Side Hair.";
        hair.Categories["Ahoge"] =
            "This is the ahoge (a single stray strand or cowlick on top). Describe its shape, size, and " +
            "direction. Examples: Small Single Ahoge, Long Curved Ahoge, Twin Ahoge.";
        hair.Categories["Extra"] =
            "These are extra or decorative hair elements. Describe their shape and placement. " +
            "Examples: Hair Bows, Braided Crown, Ribbon Accents.";
        hair.Categories["Base Hair"] =
            "This is the base hair shape under the rest. Describe the fringe and the overall " +
            "silhouette/volume. Examples: Full Round Base, Short Base with Fringe.";
    }

    private SectionProfile Define(string section, string prompt)
    {
        var p = Section(section);
        p.Prompt = prompt;
        return p;
    }

    private SectionProfile Section(string section)
    {
        if (!_sections.TryGetValue(section, out var p)) { p = new SectionProfile(); _sections[section] = p; }
        return p;
    }

    private void LoadOverrides(string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            if (root.TryGetProperty("system", out var sys) && sys.ValueKind == JsonValueKind.String)
                System = sys.GetString();
            if (!root.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Object) return;

            foreach (var prop in sections.EnumerateObject())
            {
                var profile = Section(prop.Name);
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    profile.Prompt = prop.Value.GetString();
                    continue;
                }
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                if (prop.Value.TryGetProperty("prompt", out var prompt) && prompt.ValueKind == JsonValueKind.String)
                    profile.Prompt = prompt.GetString();
                if (!prop.Value.TryGetProperty("categories", out var categories) || categories.ValueKind != JsonValueKind.Object)
                    continue;
                foreach (var cat in categories.EnumerateObject())
                    if (cat.Value.ValueKind == JsonValueKind.String)
                        profile.Categories[cat.Name] = cat.Value.GetString();
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"prompts: cannot load {file}: {e.Message}");
        }
    }
}
