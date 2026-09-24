using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;

namespace ACCcom.ViewModels;

/// <summary>
/// View-model for the theme gallery picker. Built fresh on every open from the
/// current <see cref="MainViewModel.ThemeOption"/> list so names/descriptions
/// always match the active language. Picking a card applies the theme live
/// through the callback (wired to MainViewModel.SelectedTheme).
/// </summary>
public class ThemeGalleryViewModel : ObservableObject
{
    public sealed class ThemeCard : ObservableObject
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
        public Color Accent { get; init; }
        public Color Bg { get; init; }
        public Color Surface { get; init; }
        public Color Ink { get; init; }
        public Color InkSoft { get; init; }
        public Color Good { get; init; }
        public Color Bad { get; init; }

        private bool _isCurrent;
        public bool IsCurrent
        {
            get => _isCurrent;
            set => SetField(ref _isCurrent, value);
        }
    }

    public ObservableCollection<ThemeCard> Cards { get; }

    public ICommand PickCommand { get; }

    public ThemeGalleryViewModel(
        IEnumerable<MainViewModel.ThemeOption> options,
        string selectedId,
        Action<string> onPick)
    {
        Cards = new ObservableCollection<ThemeCard>(options.Select(o => new ThemeCard
        {
            Id = o.Id,
            Name = o.Name,
            Description = o.Description,
            Accent = o.Accent,
            Bg = o.Bg,
            Surface = o.Surface,
            Ink = o.Ink,
            InkSoft = o.InkSoft,
            Good = o.Good,
            Bad = o.Bad,
            IsCurrent = o.Id == selectedId
        }));
        PickCommand = new RelayCommand(parameter =>
        {
            if (parameter is not string id) return;
            onPick(id);
            foreach (var card in Cards)
                card.IsCurrent = card.Id == id;
        });
    }
}
