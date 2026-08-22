using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using Pendulum.App.ViewModels;
using Pendulum.Core.Models;

namespace Pendulum.App.Views;

public partial class TriggersPanel : UserControl
{
    public TriggersPanel()
    {
        InitializeComponent();
        Loaded += (_, __) => EnableLiveSorting();
    }

    private void EnableLiveSorting()
    {
        if (FindResource("SortedTriggers") is not CollectionViewSource cvs)
            return;

        if (cvs.View is ICollectionViewLiveShaping liveShaping && liveShaping.CanChangeLiveSorting)
        {
            liveShaping.LiveSortingProperties.Add(nameof(TriggerTimer.TriggerAt));
            liveShaping.IsLiveSorting = true;
        }
    }

    public void OpenAddTimerDialog()
    {
        (DataContext as TriggersViewModel)?.AddCommand.Execute(null);
    }
}
