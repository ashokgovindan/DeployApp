using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeployApp.Models
{
    public class DeploymentRequest : INotifyPropertyChanged
    {
        private int _id;
        private string _rpaName = string.Empty;
        private string _requestDate = string.Empty;
        private string _changeDate = string.Empty;
        private string _changeNumber = string.Empty;
        private string _ritmNumber = string.Empty;
        private string _environment = "PROD";
        private string _status = "Requested";
        private string _requestedBy = string.Empty;
        private string _deployedDate = string.Empty;
        private string _changeDescription = string.Empty;
        private string _impactAnalysis = string.Empty;
        private string _tasksToDeploy = string.Empty;
        private string _testPlanLogPath = string.Empty;
        private string _codeReviewDocumentPath = string.Empty;
        private string _codeAnalysisPath = string.Empty;
        private string _codeMovedToTest = "No";
        private bool _isSelected;

        /// <summary>
        /// Database primary key (auto-increment). Zero for new unsaved records.
        /// </summary>
        public int Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string RpaName
        {
            get => _rpaName;
            set { _rpaName = value; OnPropertyChanged(); }
        }

        public string Deployment => $"Request {RequestDate}";

        public string RequestDate
        {
            get => _requestDate;
            set { _requestDate = value; OnPropertyChanged(); OnPropertyChanged(nameof(Deployment)); }
        }

        public string ChangeDate
        {
            get => _changeDate;
            set { _changeDate = value; OnPropertyChanged(); }
        }

        public string ChangeNumber
        {
            get => _changeNumber;
            set { _changeNumber = value; OnPropertyChanged(); }
        }

        public string RitmNumber
        {
            get => _ritmNumber;
            set { _ritmNumber = value; OnPropertyChanged(); }
        }

        public string Environment
        {
            get => _environment;
            set { _environment = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string RequestedBy
        {
            get => _requestedBy;
            set { _requestedBy = value; OnPropertyChanged(); }
        }

        public string DeployedDate
        {
            get => _deployedDate;
            set { _deployedDate = value; OnPropertyChanged(); }
        }

        public string ChangeDescription
        {
            get => _changeDescription;
            set { _changeDescription = value; OnPropertyChanged(); }
        }

        public string ImpactAnalysis
        {
            get => _impactAnalysis;
            set { _impactAnalysis = value; OnPropertyChanged(); }
        }

        public string TasksToDeploy
        {
            get => _tasksToDeploy;
            set { _tasksToDeploy = value; OnPropertyChanged(); }
        }

        public string TestPlanLogPath
        {
            get => _testPlanLogPath;
            set { _testPlanLogPath = value; OnPropertyChanged(); }
        }

        public string CodeReviewDocumentPath
        {
            get => _codeReviewDocumentPath;
            set { _codeReviewDocumentPath = value; OnPropertyChanged(); }
        }

        public string CodeAnalysisPath
        {
            get => _codeAnalysisPath;
            set { _codeAnalysisPath = value; OnPropertyChanged(); }
        }

        public string CodeMovedToTest
        {
            get => _codeMovedToTest;
            set { _codeMovedToTest = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// UI-only selection state for the checkbox column.
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Creates a deep copy of this deployment request.
        /// </summary>
        public DeploymentRequest Clone()
        {
            return new DeploymentRequest
            {
                Id = this.Id,
                RpaName = this.RpaName,
                RequestDate = this.RequestDate,
                ChangeDate = this.ChangeDate,
                ChangeNumber = this.ChangeNumber,
                RitmNumber = this.RitmNumber,
                Environment = this.Environment,
                Status = this.Status,
                RequestedBy = this.RequestedBy,
                DeployedDate = this.DeployedDate,
                ChangeDescription = this.ChangeDescription,
                ImpactAnalysis = this.ImpactAnalysis,
                TasksToDeploy = this.TasksToDeploy,
                TestPlanLogPath = this.TestPlanLogPath,
                CodeReviewDocumentPath = this.CodeReviewDocumentPath,
                CodeAnalysisPath = this.CodeAnalysisPath,
                CodeMovedToTest = this.CodeMovedToTest
            };
        }

        /// <summary>
        /// Copies all property values from another deployment request into this instance.
        /// </summary>
        public void CopyFrom(DeploymentRequest other)
        {
            RpaName = other.RpaName;
            RequestDate = other.RequestDate;
            ChangeDate = other.ChangeDate;
            ChangeNumber = other.ChangeNumber;
            RitmNumber = other.RitmNumber;
            Environment = other.Environment;
            Status = other.Status;
            RequestedBy = other.RequestedBy;
            DeployedDate = other.DeployedDate;
            ChangeDescription = other.ChangeDescription;
            ImpactAnalysis = other.ImpactAnalysis;
            TasksToDeploy = other.TasksToDeploy;
            TestPlanLogPath = other.TestPlanLogPath;
            CodeReviewDocumentPath = other.CodeReviewDocumentPath;
            CodeAnalysisPath = other.CodeAnalysisPath;
            CodeMovedToTest = other.CodeMovedToTest;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
