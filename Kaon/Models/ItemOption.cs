using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kaon.Models;

// One size or portion choice for a food package item.
// Examples: "Small tray (6-8 people)" for 450, "Whole lechon, 8 kg" for 7500, "Per kilo" for 950.
public class ItemOption
{
    public int Id { get; set; }

    public int MenuItemId { get; set; }
    public MenuItem? MenuItem { get; set; }

    [Required, StringLength(80)]
    public string Label { get; set; } = "";

    [Column(TypeName = "decimal(10,2)")]
    public decimal Price { get; set; }

    public int SortOrder { get; set; }
}
