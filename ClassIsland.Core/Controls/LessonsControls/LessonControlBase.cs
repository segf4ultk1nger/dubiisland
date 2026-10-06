using System.Windows;
using System.Windows.Controls;
using ClassIsland.Shared;
using ClassIsland.Shared.Models.Profile;

namespace ClassIsland.Core.Controls.LessonsControls;


public abstract class LessonControlBase : UserControl
{
    public static readonly DependencyProperty ClassInfoProperty = DependencyProperty.Register(
        nameof(ClassInfo), typeof(ClassInfo), typeof(LessonControlBase), new PropertyMetadata(default(ClassInfo)));

    public ClassInfo ClassInfo
    {
        get { return (ClassInfo)GetValue(ClassInfoProperty); }
        set { SetValue(ClassInfoProperty, value); }
    }

    public static readonly DependencyProperty SubjectsProperty = DependencyProperty.Register(
        nameof(Subjects), typeof(ObservableDictionary<Guid, Subject>), typeof(LessonControlBase), new PropertyMetadata(new ObservableDictionary<Guid, Subject>()));

    public ObservableDictionary<Guid, Subject> Subjects
    {
        get { return (ObservableDictionary<Guid, Subject>)GetValue(SubjectsProperty); }
        set { SetValue(SubjectsProperty, value); }
    }
}