# MyCOCBot Development TODO List

- [ ] Finalize `/channels` tree-view generator command
- [ ] Finalize `/roles` tree-view generator command

- [ ] Add commands for adding removing clans
- [ ] Add command to show clan links
- [ ] 
- [ ] Finalize `/channels` tree-view generator command
- [ ] Set up automated backup script on Oracle Linux VM

# MyCOCBot Known Bugs & Issues

This list tracks active bugs, their severity, and replication steps.

## 🔴 Active Bugs (High Priority)

- [ ] **IP Assign fails 1st time**
  - **Description**: The first execution of `/showclans` after bot reboot takes up to 7 seconds, sometimes triggering a Discord gateway timeout.
  - **Workaround**: Implemented `Await command.DeferAsync()`, but underlying connection pooling needs optimization.
  - **File**: `OracleDatabaseManager.vb`

## 🟡 Minor Issues & Edge Cases


## 🟢 Resolved Bugs

# Changelog

All notable changes to the **MyCOCBot** project will be documented in this file.

## - 2026-05-28
### Added
- Created a complete asynchronous `OracleDatabaseManager` module.
- Added `/showclans` slash command to fetch Clash of Clans data from the Oracle Cloud DB.

### Changed
- Removed Windows-specific `MsgBox` alerts and replaced them with Linux-compatible console logging.