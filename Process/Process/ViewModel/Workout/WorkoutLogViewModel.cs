﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using GalaSoft.MvvmLight;
using LiveCharts;
using LiveCharts.Wpf;
using Microsoft.EntityFrameworkCore;
using Process.Data;
using Process.Dialogs;
using Process.Dialogs.Workout;
using Process.Models.Common;
using Process.Models.Workout;
using Process.ViewModel.App;
using static Process.Helpers.ObservableCollectionHelper;

namespace Process.ViewModel.Workout
{
    public class WorkoutLogViewModel : ViewModelBase
    {
        public WorkoutLogViewModel()
        {
            // General Commands
            AddWorkoutCommand = new RelayCommand(p => AddWorkout());
            AddWorkoutPlanCommand = new RelayCommand(p => AddWorkoutPlan());
            WorkoutPlanDeleteCommand = new RelayParameterizedCommand(WorkoutPlanDelete);
            AddWorkoutTargetsCommand = new RelayParameterizedCommand(AddWorkoutTargets);
            AddWorkoutLogCommand = new RelayParameterizedCommand(AddWorkoutLog);
            ShowTargetLogGraphCommand = new RelayParameterizedCommand(ShowTargetLogGraph);
            ShowMeasurementsCommand = new RelayParameterizedCommand(ShowMeasurements);
            SetIsBreakCommand = new RelayParameterizedCommand(SetIsBreak);
            YearChangedCommand = new RelayCommand(p => LoadWorkoutPlans());

            LoadYears();
            LoadWorkoutPlans();
        }

        #region Commands

        // General Commands

        public ICommand AddWorkoutCommand { get; set; }
        public ICommand AddWorkoutPlanCommand { get; set; }
        public ICommand WorkoutPlanDeleteCommand { get; set; }
        public ICommand AddWorkoutTargetsCommand { get; set; }
        public ICommand AddWorkoutLogCommand { get; set; }
        public ICommand ShowTargetLogGraphCommand { get; set; }
        public ICommand ShowMeasurementsCommand { get; set; }
        public ICommand SetIsBreakCommand { get; set; }
        public ICommand YearChangedCommand { get; set; }

        // Plan Context Menu Commands

        public ICommand ClonePlanCommand { get; set; }


        // Plan Day Context Menu Commands

        public ICommand ShowWorkoutDayCommand { get; set; }

        #endregion

        #region Public Properties

        public ObservableCollection<WorkoutPlanItem> WorkoutPlanItems { get; set; } = new ObservableCollection<WorkoutPlanItem>();
        public SeriesCollection YearNotesGraph { get; set; } = new SeriesCollection();
        public List<string> Labels { get; set; } = new List<string>();
        public Func<double, string> YFormatter { get; set; }

        /// <summary>
        /// Selected year
        /// </summary>
        public Year TargetYear { get; set; } = new Year();

        /// <summary>
        /// List of years
        /// </summary>
        public ObservableCollection<Year> Years { get; set; } = new ObservableCollection<Year>();
        #endregion

        public WorkoutDayItem WorkoutDayItem { get; set; }

        #region Methods

        public async Task LoadYears()
        {
            using var db = new AppDbContext();

            Years = db.WorkoutPlans
            .GroupBy(x => x.AddedDate.Year)
            .Select(x => new Year { YearNumber = x.Key })
            .ToObservableCollection();

            if (Years.Count == 0)
            {
                Years.Add(new Year 
                {
                    YearNumber = DateTime.Now.Year
                });
            }

            TargetYear = Years.First();
        }

        /// <summary>
        /// Load all workout plans
        /// </summary>
        private void LoadWorkoutPlans()
        {
            using var db = new AppDbContext();

            var workoutPlanItems = db.WorkoutPlans
            .Where(wPlan => EF.Functions.Like(wPlan.AddedDate.Year.ToString(), TargetYear.YearNumber.ToString()))
            .Select(wPlan => new WorkoutPlanItem
            {
                WorkoutPlan = wPlan,
                IsExpired = wPlan.ExpireDate < DateTime.Now,

                WorkoutResultItem = db.WorkoutResults.Where(wResult => wResult.WorkoutPlanId == wPlan.Id)
                .Select(wResult => new WorkoutResultItem
                {
                    WorkoutResult = wResult,
                    WorkoutMeasurements = db.WorkoutMeasurements
                    .Where(wMeasurement => wMeasurement.WorkoutResultId == wResult.Id)
                    .ToObservableCollection()
                })
                .FirstOrDefault()
                ?? new WorkoutResultItem
                {
                    WorkoutResult = new WorkoutResult
                    {
                        WorkoutPlanId = wPlan.Id
                    }
                },

                WorkoutDayItems = db.WorkoutDays.Where(wDay => wDay.WorkoutPlanId == wPlan.Id)
                .Select(wDay => new WorkoutDayItem
                {
                    WorkoutDay = wDay,

                    WorkoutTargetItems = db.WorkoutTargets.Where(wTarget => wTarget.WorkoutPlanId == wPlan.Id)
                    .Select(wTarget => new WorkoutTargetItem
                    {
                        WorkoutTarget = wTarget,
                        Workout = db.Workouts.First(x => x.Id == wTarget.WorkoutId) ?? new Models.Workout.Workout(),
                        WorkoutLog = db.WorkoutLogs.FirstOrDefault(x => x.WorkoutDayId == wDay.Id && x.WorkoutTargetId == wTarget.Id)
                        ?? new WorkoutLog
                        {
                            AddedDate = DateTime.Now,
                            WorkoutDayId = wDay.Id,
                            WorkoutTargetId = wTarget.Id,
                            WorkoutId = wTarget.WorkoutId
                        }
                    })
                    .ToObservableCollection(),

                    WorkoutDayCompleteCount = db.WorkoutLogs.Count(x => x.WorkoutDayId == wDay.Id && x.IsCompleted),

                    ShowWorkoutDayCommand = new RelayParameterizedCommand(ShowWorkoutDay),
                    SetAllDoneCommand = new RelayParameterizedCommand(SetAllWorkoutsDone),
                    SetAllUndoneCommand = new RelayParameterizedCommand(SetAllWorkoutsUndone)
                }).ToObservableCollection(),
                WorkoutPlanCompleteCount = db.WorkoutDays.Count(x => x.WorkoutPlanId == wPlan.Id && x.IsCompleted)
            })
            .OrderByDescending(wPlan => wPlan.WorkoutPlan.Id)
            .ToObservableCollection();

            foreach (var planItem in workoutPlanItems)
            {
                // current plan note
                planItem.PlanNote = planItem.WorkoutDayItems
                .Where(x => !x.WorkoutDay.IsBreak && x.WorkoutDayCompleteCount > 0)
                .Sum(x => x.WorkoutDayCompleteCount);

                // target plan note
                planItem.PlanTarget = planItem.WorkoutDayItems
                .Where(x => !x.WorkoutDay.IsBreak && x.WorkoutDay.IsCompleted)
                .Count() *
                planItem.WorkoutDayItems.First().WorkoutTargetItems.Count();
            }

            WorkoutPlanItems = workoutPlanItems;

            LoadYearNotesGraph();
        }

        /// <summary>
        /// Reload workout plan item
        /// </summary>
        /// <param name="workoutPlanId"></param>
        /// <returns></returns>
        private WorkoutPlanItem LoadWorkoutPlanItem(long workoutPlanId)
        {
            using var db = new AppDbContext();

            return db.WorkoutPlans.Where(wPlan => wPlan.Id == workoutPlanId).Select(wPlan => new WorkoutPlanItem
            {
                WorkoutPlan = wPlan,
                IsExpired = wPlan.ExpireDate < DateTime.Now,

                WorkoutDayItems = db.WorkoutDays.Where(wDay => wDay.WorkoutPlanId == wPlan.Id)
                    .Select(wDay => new WorkoutDayItem
                    {
                        WorkoutDay = wDay,

                        WorkoutTargetItems = db.WorkoutTargets.Where(wTarget => wTarget.WorkoutPlanId == wPlan.Id)
                        .Select(wTarget => new WorkoutTargetItem
                        {
                            WorkoutTarget = wTarget,
                            Workout = db.Workouts.First(x => x.Id == wTarget.WorkoutId) ?? new Models.Workout.Workout(),
                            WorkoutLog = db.WorkoutLogs.FirstOrDefault(x => x.WorkoutDayId == wDay.Id && x.WorkoutTargetId == wTarget.Id)
                            ?? new WorkoutLog
                            {
                                AddedDate = DateTime.Now,
                                WorkoutDayId = wDay.Id,
                                WorkoutTargetId = wTarget.Id,
                                WorkoutId = wTarget.WorkoutId
                            }
                        }).ToObservableCollection(),

                        WorkoutDayCompleteCount = db.WorkoutLogs.Count(x => x.WorkoutDayId == wDay.Id)
                    }).ToObservableCollection(),
                WorkoutPlanCompleteCount = db.WorkoutDays.Count(x => x.IsCompleted)
            }).Single();
        }

        private void LoadYearNotesGraph()
        {
            Labels = new List<string>();
            YearNotesGraph = new SeriesCollection
            {
                new ColumnSeries
                {
                    Title = "Plan Note",
                    Values = new ChartValues<long> { }
                },
                new ColumnSeries
                {
                    Title = "Plan Target",
                    Values = new ChartValues<long> { }
                }
            };

            foreach (var workoutPlanItems in WorkoutPlanItems)
            {
                YearNotesGraph[0].Values.Add(workoutPlanItems.PlanNote);
                YearNotesGraph[1].Values.Add(workoutPlanItems.PlanTarget);
                Labels.Add(workoutPlanItems.WorkoutPlan.Title);
            }
        }

        #endregion

        #region Command Methods

        #region General Command Methods

        /// <summary>
        /// Add workout
        /// </summary>
        public void AddWorkout()
        {
            var dialog = new WorkoutInputDialog();

            dialog.ShowDialogWindow(new WorkoutInputViewModel(dialog));
        }

        /// <summary>
        /// Add new workout plan and days
        /// </summary>
        public void AddWorkoutPlan()
        {
            var dialog = new WorkoutPlanInputDialog();

            dialog.Closing += (s, e) =>
            {
                if (dialog.DataContext is WorkoutPlanInputViewModel vm && vm.WorkoutPlanItem.WorkoutPlan.Id > 0)
                {
                    WorkoutPlanItems.Insert(0, vm.WorkoutPlanItem);
                }
            };

            dialog.ShowDialogWindow(new WorkoutPlanInputViewModel(dialog));
        }

        /// <summary>
        /// Delete selected workout plan and items
        /// </summary>
        /// <param name="sender">Sender button</param>
        public void WorkoutPlanDelete(object sender)
        {
            if (sender == null || !(sender is Button button)) return;
            if (!(button.DataContext is WorkoutPlanItem workoutPlanItem)) return;

            var dialog = new DeleteDialog();

            dialog.Closing += (send, args) =>
            {
                if (dialog.DataContext is DeleteDialogViewModel vm && vm.Result)
                {
                    using var db = new AppDbContext();
                    db.WorkoutPlans.Remove(workoutPlanItem.WorkoutPlan);
                    db.SaveChanges();

                    WorkoutPlanItems.Remove(workoutPlanItem);
                }
            };

            dialog.ShowDialogWindow(new DeleteDialogViewModel(dialog, "Delete Workout Plan", workoutPlanItem.WorkoutPlan.Title));
        }

        /// <summary>
        /// Add workout targets to plan (reloads plan and items)
        /// </summary>
        /// <param name="sender">Sender button</param>
        public void AddWorkoutTargets(object sender)
        {
            if (sender == null || !(sender is Button button)) return;
            if (!(button.DataContext is WorkoutPlanItem workoutPlanItem)) return;

            var dialog = new WorkoutTargetInputDialog();

            dialog.Closing += (sen, args) =>
            {
                if (!(dialog.DataContext is WorkoutTargetInputViewModel vm) || vm.TargetCount <= 0) return;

                for (var i = 0; i < WorkoutPlanItems.Count; i++)
                {
                    if (WorkoutPlanItems[i].WorkoutPlan.Id == workoutPlanItem.WorkoutPlan.Id)
                    {
                        WorkoutPlanItems[i] = LoadWorkoutPlanItem(workoutPlanItem.WorkoutPlan.Id);
                    }
                }
            };

            dialog.ShowDialogWindow(new WorkoutTargetInputViewModel(dialog, workoutPlanItem)); ;
        }

        /// <summary>
        /// Add completed workout log
        /// </summary>
        /// <param name="sender">Sender button</param>
        public void AddWorkoutLog(object sender)
        {
            if (sender == null || !(sender is Button button)) return;
            if (!(button.DataContext is WorkoutDayItem workoutDayItem)) return;

            var dialog = new WorkoutLogInputDialog();

            dialog.Closing += (s, e) =>
            {
                var workoutPlanItem = WorkoutPlanItems.Single(x => x.WorkoutPlan.Id == workoutDayItem.WorkoutDay.WorkoutPlanId);

                if (workoutPlanItem != null && workoutPlanItem.WorkoutDayItems != null && workoutPlanItem.WorkoutDayItems.Count > 0)
                {
                    workoutPlanItem.WorkoutPlanCompleteCount = workoutPlanItem.WorkoutDayItems.Count(x => x.WorkoutDay.IsCompleted);

                    if (workoutPlanItem.WorkoutPlanCompleteCount > 0 && workoutPlanItem.WorkoutPlanCompleteCount == workoutPlanItem.WorkoutDayItems.Count)
                    {
                        workoutPlanItem.WorkoutPlan.IsCompleted = true;

                        using var db = new AppDbContext();
                        db.WorkoutPlans.Update(workoutPlanItem.WorkoutPlan);
                        db.SaveChanges();
                    }
                }
            };

            dialog.ShowDialogWindow(new WorkoutLogInputViewModel(dialog, workoutDayItem));
        }

        /// <summary>
        /// Show target-log graph
        /// </summary>
        /// <param name="sender">Sender button</param>
        public void ShowTargetLogGraph(object sender)
        {
            if (sender == null || !(sender is Button button)) return;
            if (!(button.DataContext is WorkoutPlanItem workoutPlanItem)) return;

            var dialog = new WorkoutTargetLogGraphDialog();
            dialog.ShowDialogWindow(new WorkoutTargetLogGraphViewModel(dialog, workoutPlanItem)); ;
        }

        /// <summary>
        /// Show measurement reults
        /// </summary>
        /// <param name="sender"></param>
        public void ShowMeasurements(object sender)
        {
            var workoutPlanItem = (sender as Button).DataContext as WorkoutPlanItem;

            var dialog = new WorkoutResultDialog();
            dialog.ShowDialogWindow(new WorkoutResultInputViewModel(dialog, workoutPlanItem.WorkoutResultItem)); ;
        }

        /// <summary>
        /// Set break day
        /// </summary>
        /// <param name="sender"></param>
        public void SetIsBreak(object sender)
        {
            var workoutDayItem = (sender as CheckBox).DataContext as WorkoutDayItem;

            using var db = new AppDbContext();
            db.WorkoutDays.Update(workoutDayItem.WorkoutDay);
            db.SaveChanges();
        }

        #endregion

        // Plan Day Context Menu Commands

        public void ShowWorkoutDay(object sender)
        {
            var workoutDayItem = (WorkoutDayItem)(sender as Grid).DataContext;

            var dialog = new WorkoutDayGraphDialog();
            dialog.ShowDialogWindow(new WorkoutDayGraphViewModel(dialog, workoutDayItem));
        }

        /// <summary>
        /// Set all workouts done
        /// </summary>
        public void SetAllWorkoutsDone(object sender)
        {
            WorkoutDayItem = (WorkoutDayItem)(sender as Grid).DataContext;

            foreach (var workoutTargetItem in WorkoutDayItem.WorkoutTargetItems)
            {
                workoutTargetItem.WorkoutLog.Sets = workoutTargetItem.WorkoutTarget.RequiredSets;
                workoutTargetItem.WorkoutLog.Repeats = workoutTargetItem.WorkoutTarget.RequiredRepeats;
                workoutTargetItem.WorkoutLog.IsCompleted = true;

                using var db = new AppDbContext();

                _ = workoutTargetItem.WorkoutLog.Id > 0 ?
                db.WorkoutLogs.Update(workoutTargetItem.WorkoutLog) :
                db.WorkoutLogs.Add(workoutTargetItem.WorkoutLog);

                WorkoutDayItem.WorkoutDayCompleteCount = WorkoutDayItem.WorkoutTargetItems.Count;

                if (WorkoutDayItem.WorkoutTargetItems.Count > 0
                && WorkoutDayItem.WorkoutTargetItems.Count == WorkoutDayItem.WorkoutDayCompleteCount)
                {
                    WorkoutDayItem.WorkoutDay.IsCompleted = true;
                    db.WorkoutDays.Update(WorkoutDayItem.WorkoutDay);
                }
                else if (WorkoutDayItem.WorkoutDay.IsCompleted)
                {
                    WorkoutDayItem.WorkoutDay.IsCompleted = false;
                    db.WorkoutDays.Update(WorkoutDayItem.WorkoutDay);
                }

                db.SaveChanges();
            }

            for (int i = 0; i < WorkoutPlanItems.Count; i++)
            {
                if (WorkoutPlanItems[i].WorkoutDayItems.Contains(WorkoutDayItem))
                {
                    WorkoutPlanItems[i].WorkoutPlanCompleteCount += 1;
                    WorkoutPlanItems[i].PlanNote += WorkoutDayItem.WorkoutDayCompleteCount;
                    return;
                }
            }
        }

        public void SetAllWorkoutsUndone(object sender)
        {
            WorkoutDayItem = (WorkoutDayItem)(sender as Grid).DataContext;

            foreach (var workoutTargetItem in WorkoutDayItem.WorkoutTargetItems)
            {
                workoutTargetItem.WorkoutLog.Sets = 0;
                workoutTargetItem.WorkoutLog.Repeats = 0;
                workoutTargetItem.WorkoutLog.IsCompleted = false;

                using var db = new AppDbContext();

                _ = workoutTargetItem.WorkoutLog.Id > 0 ?
                db.WorkoutLogs.Update(workoutTargetItem.WorkoutLog) :
                db.WorkoutLogs.Add(workoutTargetItem.WorkoutLog);

                WorkoutDayItem.WorkoutDayCompleteCount = 0;

                if (WorkoutDayItem.WorkoutTargetItems.Count > 0 && WorkoutDayItem.WorkoutDayCompleteCount == 0)
                {
                    WorkoutDayItem.WorkoutDay.IsCompleted = false;
                    db.WorkoutDays.Update(WorkoutDayItem.WorkoutDay);
                }
                else if (WorkoutDayItem.WorkoutDay.IsCompleted)
                {
                    WorkoutDayItem.WorkoutDay.IsCompleted = true;
                    db.WorkoutDays.Update(WorkoutDayItem.WorkoutDay);
                }

                db.SaveChanges();
            }

            for (int i = 0; i < WorkoutPlanItems.Count; i++)
            {
                if (WorkoutPlanItems[i].WorkoutDayItems.Contains(WorkoutDayItem))
                {
                    WorkoutPlanItems[i].WorkoutPlanCompleteCount -= 1;
                    WorkoutPlanItems[i].PlanNote -= WorkoutDayItem.WorkoutTargetItems.Count();
                    return;
                }
            }
        }

        #endregion
    }
}