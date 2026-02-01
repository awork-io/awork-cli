# CLI HIERARCHY CONCEPT

Goal: reduce top-level noise while keeping CLI intuitive and agent-friendly.  
Approach: map Swagger tags into a small set of top-level domains.  
Legacy aliases can remain (current top-level tags) while new hierarchy is introduced.

## Canonical Structure (Top-Level Domains)

```
awork
├─ auth
├─ users
├─ tasks
├─ projects
├─ times
├─ workspace
├─ documents
├─ files
├─ search
├─ integrations
└─ automation
```

## Tag → Domain Mapping

```
auth
  Accounts
  ClientApplications

users
  Users
  ApiUsers
  Invitations
  UserTags
  UserFiles
  UserCapacities

tasks
  Tasks
  PrivateTasks
  AssignedTasks
  TaskComments
  TaskFiles
  TaskTags
  TaskLists
  TaskSchedules
  TaskStatuses
  TaskViews
  TaskBundles
  TaskDependencies
  TaskDependencyTemplates
  TaskTemplates
  TaskTemplateFiles
  ChecklistItems

projects
  Projects
  ProjectTasks
  ProjectMembers
  ProjectComments
  ProjectFiles
  ProjectTags
  ProjectStatuses
  ProjectRoles
  ProjectTypes
  ProjectMilestones
  ProjectMilestoneTemplates
  ProjectTemplates
  ProjectTemplateFiles
  ProjectTemplateTags
  Project Automations
  Project Template Automations
  Retainers

times
  TimeEntries
  TimeBookings
  TimeReports
  TimeTracking
  Workload
  Absences

workspace
  Workspaces
  WorkspaceFiles
  WorkspaceAbsences
  Teams
  Roles
  Permissions
  CustomFields
  TypeOfWork
  Companies
  CompanyFiles
  CompanyTags
  Dashboards
  Activities
  AbsenceRegions

documents
  Documents
  DocumentFiles
  DocumentComments
  DocumentSpaces

files
  Files
  FileUpload
  TemporaryFiles
  SharedFiles
  Images
  CommentFiles

search
  Search

integrations
  Webhooks

automation
  Autopilot
```

## Example Commands

```
awork users list
awork users invitations create

awork tasks create
awork tasks tags update

awork projects list
awork projects tags list

awork times absences list
awork times entries list

awork workspace teams list
awork workspace roles list
awork workspace absence-regions list

awork documents list
awork files upload
awork search query
awork auth login
```

## Notes

- `AbsenceRegions` lives under `workspace` (global config), not `times`.
- `Absences` live under `times` (per-user leave/time-off).
- Legacy aliases can keep current `awork <tag> ...` paths to avoid breakage.
