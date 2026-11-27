using System.Windows;
using System.Windows.Controls;
using MyDriver.App.ViewModels;

namespace MyDriver.App.Views;

public partial class DevicesView : UserControl
{
    public DevicesView()
    {
        InitializeComponent();
    }

    private void DeviceTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is DevicesViewModel vm)
        {
            vm.OnTreeSelectionChanged(e.NewValue);
        }
    }
}
