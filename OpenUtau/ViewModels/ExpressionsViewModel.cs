using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    public class ExpressionBuilder : ReactiveObject {
        [Reactive] public string Name { get; set; }
        [Reactive] public string Abbr { get; set; }
        [Reactive] public int ExpressionType { get; set; }
        [Reactive] public float Min { get; set; }
        [Reactive] public float Max { get; set; }
        [Reactive] public float DefaultValue { get; set; }
        [Reactive] public float CustomeDefaultValue { get; set; }
        [Reactive] public bool IsFlag { get; set; }
        [Reactive] public string Flag { get; set; }
        [Reactive] public string OptionValues { get; set; }
        [Reactive] public bool SkipOutputIfDefault { get; set; } = false;

        public bool IsCustom => isCustom.Value;
        public bool IsRemovable => isRemovable.Value;
        public bool IsNumerical => isNumerical.Value;
        public bool IsCurve => isCurve.Value;
        public bool IsOptions => isOptions.Value;

        private ObservableAsPropertyHelper<bool> isCustom;
        private ObservableAsPropertyHelper<bool> isRemovable;
        private ObservableAsPropertyHelper<bool> isNumerical;
        private ObservableAsPropertyHelper<bool> isCurve;
        private ObservableAsPropertyHelper<bool> isOptions;

        public ExpressionBuilder(UExpressionDescriptor descriptor)
            : this(descriptor.name, descriptor.abbr, descriptor.min, descriptor.max, descriptor.isFlag, descriptor.flag,
                  descriptor.options == null ? string.Empty : string.Join(',', descriptor.options)) {
            ExpressionType = (int)descriptor.type;
            DefaultValue = descriptor.defaultValue;
            CustomeDefaultValue = descriptor.CustomDefaultValue;
            SkipOutputIfDefault = descriptor.skipOutputIfDefault;
        }

        public ExpressionBuilder()
            : this("new expression", string.Empty, 0, 100, false, string.Empty, string.Empty) {
        }

        public ExpressionBuilder(string name, string abbr, float min, float max, bool isFlag, string flag, string optionValues) {
            Name = name;
            Abbr = abbr;
            Min = min;
            Max = max;
            IsFlag = isFlag;
            Flag = flag;
            OptionValues = optionValues;

            this.WhenAnyValue(x => x.Abbr)
                .Select(abbr => !Core.Format.Ustx.required.Contains(abbr))
                .ToProperty(this, x => x.IsCustom, out isCustom);
            this.WhenAnyValue(x => x.Abbr)
                .Select(abbr => !Core.Format.Ustx.required.Contains(abbr) || ExpressionsViewModel.isTrackOverride)
                .ToProperty(this, x => x.IsRemovable, out isRemovable);
            this.WhenAnyValue(x => x.ExpressionType)
                .Select(type => type == 0) // Numerical
                .ToProperty(this, x => x.IsNumerical, out isNumerical);
            this.WhenAnyValue(x => x.ExpressionType)
                .Select(type => type == 1) // Options
                .ToProperty(this, x => x.IsOptions, out isOptions);
            this.WhenAnyValue(x => x.ExpressionType)
                .Select(type => type == 2) // Curve
                .ToProperty(this, x => x.IsCurve, out isCurve);
        }

        public string[]? Validate() {
            if (string.IsNullOrWhiteSpace(Name)) {
                return new string[] { "Name must be set.", "<translate:errors.expression.name>" };
            }
            if (string.IsNullOrWhiteSpace(Abbr)) {
                return new string[] { "Abbreviation must be set.", "<translate:errors.expression.abbrset>" };
            }
            if (ExpressionType == 0) { // Numerical
                if (Abbr.Trim().Length < 1 || Abbr.Trim().Length > 4) {
                    return new string[] { "Abbreviation must be between 1 and 4 characters long.", $"<translate:errors.expression.abbrlong>: {Name}" };
                }
                if (Min >= Max) {
                    return new string[] { "Min must be smaller than max.", $"<translate:errors.expression.min>: {Name}" };
                }
                if (DefaultValue < Min || DefaultValue > Max) {
                    return new string[] { "Default value must be between min and max.", $"<translate:errors.expression.default>: {Name}" };
                }
                if (CustomeDefaultValue < Min || CustomeDefaultValue > Max) {
                    return new string[] { "Project/Track default value must be between min and max.", $"<translate:errors.expression.customDefault>: {Name}" };
                }
            }
            return null;
        }

        public UExpressionDescriptor Build() {
            switch ((UExpressionType)ExpressionType) {
                case UExpressionType.Numerical:
                    return new UExpressionDescriptor(Name.Trim(), Abbr.Trim().ToLower(), Min, Max, DefaultValue, Flag, CustomeDefaultValue, SkipOutputIfDefault);
                case UExpressionType.Options:
                    return new UExpressionDescriptor(Name.Trim(), Abbr.Trim().ToLower(), IsFlag, OptionValues.Split(','));
                case UExpressionType.Curve:
                    return new UExpressionDescriptor(Name.Trim(), Abbr.Trim().ToLower(), Min, Max, DefaultValue) {
                        type = UExpressionType.Curve,
                    };
            }
            throw new Exception("Unexpected expression type");
        }

        public override string ToString() => Name;
    }

    public class ExpressionsViewModel : ViewModelBase {
        [Reactive] public string WindowTitle { get; set; } = "Expressions";
        [Reactive] public bool IsTrackOverride { get; set; }
        [Reactive] public string CustomDefaultLabel { get; set; } = ThemeManager.GetString("exps.projectdefault");

        public ReadOnlyObservableCollection<ExpressionBuilder> Expressions => expressions;
        public ExpressionBuilder? Expression {
            get => expression;
            set {
                this.RaiseAndSetIfChanged(ref expression, value);
                this.RaisePropertyChanged(nameof(IsSelected));
            }
        }
        public ObservableCollection<ExpressionBuilder>? SelectExpressions => selectexpressions;
        public bool IsSwitchVisible { get; } = false;
        public static bool isTrackOverride = false;
        public bool IsSelected => expression != null;

        public IReadOnlyList<MenuItemViewModel>? AddMenuItems { get; set; }
        public ReactiveCommand<ExpressionBuilder, Unit> AddItemCommand { get; }

        private ReadOnlyObservableCollection<ExpressionBuilder> expressions;
        private ExpressionBuilder? expression;
        private ObservableCollection<ExpressionBuilder>? selectexpressions;
        private ObservableCollectionExtended<ExpressionBuilder> expressionsSourceProject;
        private ObservableCollectionExtended<ExpressionBuilder> expressionsSourceTrack;
        private UTrack? track;

        public ExpressionsViewModel(UTrack? track = null) {
            selectexpressions = new ObservableCollection<ExpressionBuilder>();
            expressionsSourceProject = new ObservableCollectionExtended<ExpressionBuilder>();
            expressionsSourceProject.AddRange(DocManager.Inst.Project.expressions.Select(pair => new ExpressionBuilder(pair.Value)));
            expressionsSourceTrack = new ObservableCollectionExtended<ExpressionBuilder>();
            if (track != null) {
                IsSwitchVisible = true;
                IsTrackOverride = true;
                this.track = track;
                expressionsSourceTrack.AddRange(track.TrackExpressions.Select(descriptor => new ExpressionBuilder(descriptor)));
            }
            expressionsSourceProject.ToObservableChangeSet()
                .Bind(out expressions)
                .Subscribe();
            SetExpressionsList();
            AddItemCommand = ReactiveCommand.Create<ExpressionBuilder>(exp => {
                var newExpression = new ExpressionBuilder(exp.Build());
                expressionsSourceTrack.Add(newExpression);
                Expression = newExpression;
            });
        }

        private void SetExpressionsList() {
            selectexpressions?.Clear();
            isTrackOverride = IsTrackOverride;
            if (IsTrackOverride) { // Track
                if (track != null) {
                    WindowTitle = $"{ThemeManager.GetString("exps.track")}: {track.TrackName}";
                    CustomDefaultLabel = ThemeManager.GetString("exps.trackdefault");
                    expressionsSourceTrack.ToObservableChangeSet()
                        .Bind(out expressions)
                        .Subscribe();
                }
            } else { // Project
                if (IsSwitchVisible) {
                    WindowTitle = ThemeManager.GetString("exps.project");
                } else {
                    WindowTitle = ThemeManager.GetString("exps.caption");
                }
                CustomDefaultLabel = ThemeManager.GetString("exps.projectdefault");
                expressionsSourceProject.ToObservableChangeSet()
                    .Bind(out expressions)
                    .Subscribe();
            }
            this.RaisePropertyChanged(nameof(Expressions));
            if (Expressions.Count > 0) {
                Expression = Expressions[0];
            }
        }

        public void Apply() {
            var invalid = expressionsSourceProject.FirstOrDefault(builder => builder.Validate() != null);
            if (invalid != null) {
                Expression = invalid;
                string[] validate = invalid.Validate()!;
                throw new MessageCustomizableException(validate[0], validate[1], new ArgumentException(validate[0]), false);
            }
            var abbrs = expressionsSourceProject.GroupBy(builder => builder.Abbr).Where(g => g.Count() > 1).Select(g => g.Key);
            if (abbrs.Count() > 0) {
                throw new MessageCustomizableException("", $"<translate:errors.expression.abbrunique>: {string.Join(", ", abbrs)}", new ArgumentException($"Abbreviations must be unique: {string.Join(", ", abbrs)}"), false);
            }
            var flags = expressionsSourceProject.Where(builder => !string.IsNullOrEmpty(builder.Flag)).GroupBy(builder => builder.Flag).Where(g => g.Count() > 1).Select(g => g.Key);
            if (flags.Count() > 0) {
                throw new MessageCustomizableException("", $"<translate:errors.expression.flagunique>: {string.Join(", ", flags)}", new ArgumentException($"Flags must be unique: {string.Join(", ", flags)}"), false);
            }

            if (track != null) {
                invalid = expressionsSourceTrack.FirstOrDefault(builder => builder.Validate() != null);
                if (invalid != null) {
                    Expression = invalid;
                    string[] validate = invalid.Validate()!;
                    throw new MessageCustomizableException(validate[0], validate[1], new ArgumentException(validate[0]), false);
                }
                abbrs = expressionsSourceTrack.GroupBy(builder => builder.Abbr).Where(g => g.Count() > 1).Select(g => g.Key);
                if (abbrs.Count() > 0) {
                    throw new MessageCustomizableException("", $"<translate:errors.expression.abbrunique>: {string.Join(", ", abbrs)}", new ArgumentException($"Abbreviations must be unique: {string.Join(", ", abbrs)}"), false);
                }
                flags = expressionsSourceTrack.Where(builder => !string.IsNullOrEmpty(builder.Flag)).GroupBy(builder => builder.Flag).Where(g => g.Count() > 1).Select(g => g.Key);
                if (flags.Count() > 0) {
                    throw new MessageCustomizableException("", $"<translate:errors.expression.flagunique>: {string.Join(", ", flags)}", new ArgumentException($"Flags must be unique: {string.Join(", ", flags)}"), false);
                }
                DocManager.Inst.StartUndoGroup("command.track.exp");
                DocManager.Inst.ExecuteCmd(new ConfigureExpressionsCommand(
                    DocManager.Inst.Project, 
                    expressionsSourceProject.Select(builder => builder.Build()).ToArray(),
                    track,
                    expressionsSourceTrack.Select(builder => builder.Build()).ToArray()));
                DocManager.Inst.EndUndoGroup();
                return;
            }
            DocManager.Inst.StartUndoGroup("command.project.exp");
            DocManager.Inst.ExecuteCmd(new ConfigureExpressionsCommand(DocManager.Inst.Project, expressionsSourceProject.Select(builder => builder.Build()).ToArray()));
            DocManager.Inst.EndUndoGroup();
        }

        public void Add() {
            if (!IsTrackOverride) {
                var newExpression = new ExpressionBuilder();
                expressionsSourceProject.Add(newExpression);
                Expression = newExpression;
            } else {
                var items = new List<MenuItemViewModel>();
                items.AddRange(expressionsSourceProject
                    .Where(exp => !expressionsSourceTrack.Any(e => e.Abbr == exp.Abbr))
                    .Select(exp => new SingerMenuItemViewModel() {
                        Header = $"{exp.Name}: {exp.Abbr}",
                        Command = AddItemCommand,
                        CommandParameter = exp,
                    }));
                items.Add(new SingerMenuItemViewModel() {
                    Header = "new expression",
                    Command = AddItemCommand,
                    CommandParameter = new ExpressionBuilder(),
                });
                AddMenuItems = items;
                this.RaisePropertyChanged(nameof(AddMenuItems));
            }
        }

        public void Remove() {
            if (SelectExpressions != null) {
                var selectedItems = SelectExpressions.ToList();
                foreach (var expression in selectedItems) {
                    if (expression.IsRemovable) {
                        if (IsTrackOverride) {
                            expressionsSourceTrack.Remove(expression);
                        } else {
                            expressionsSourceProject.Remove(expression);
                        }
                    }
                }
            } else if (Expression != null) {
                if (IsTrackOverride) {
                    expressionsSourceTrack.Remove(Expression);
                } else {
                    expressionsSourceProject.Remove(Expression);
                }
            }
        }

        public void GetSuggestions() {
            foreach (var track in DocManager.Inst.Project.tracks) {
                if (track.RendererSettings.Renderer == null) {
                    continue;
                }
                var suggestions = track.RendererSettings.Renderer.GetSuggestedExpressions(track.Singer, track.RendererSettings);
                if (suggestions == null) {
                    continue;
                }
                foreach (var suggestion in suggestions) {
                    //Add if not already in the list
                    if (!expressionsSourceProject.Any(builder => builder.Abbr == suggestion.abbr)) {
                        expressionsSourceProject.Add(new ExpressionBuilder(suggestion));
                    }
                }
            }
        }

        public void OnClickProject() {
            if (!IsTrackOverride) { // track -> project
                SetExpressionsList();
            } else {
                IsTrackOverride = false;
            }
        }

        public void OnClickTrack() {
            if (IsTrackOverride) { // project -> track
                SetExpressionsList();
            } else {
                IsTrackOverride = true;
            }
        }
    }
}
