# 📜 MyCOCBot - Slash Commands Documentation

All interactions within **MyCOCBot** are implemented as modern **Global/Guild Slash Commands**. To ensure maximum stability and prevent connection drops, commands that interact with the Oracle Database or local files automatically utilize asynchronous decoupling via `command.DeferAsync()`.

## 👥 Public Commands (Available to Everyone)

These commands can be used by any member of the server to check system health or retrieve data from the database.


| Command | Arguments | Data Source | Description |
| :--- | :--- | :--- | :--- |
| `/ping` | *None* | Discord API & Oracle DB | Checks the live connection latency to the Discord Gateway and executes a fast asynchronous handshake test with the Oracle Server. |
| `/showclans` | *None* | Oracle DB (`clans`) | Dynamically queries and displays all registered Clash of Clans clans from your Oracle Database, formatted with names and tags. |
| `/showlogs` | *None* | Oracle DB (`bot_logs`) | Fetches and displays a secure chronological embed box showing the last 5 executed bot actions from the logging table. |
| `/roles` | *None* | Guild Cache | Compiles, sorts, and prints the complete server role hierarchy, ordered descending by their actual position in the server settings. |
| `/channels` | *None* | Guild Cache | Parses and generates a clean, visual text-based directory tree of all server categories, text channels, and voice channels. |

## 🛠️ Management & Layout Commands

These commands handle advanced layouts, dynamic content delivery, and server organization.


| Command | Arguments | Details | Description |
| :--- | :--- | :--- | :--- |
| `/template` | `filename` (String) | Local File System | Reads a structured JSON template from the internal `/templates/` directory and renders it into a visual Discord Rich Embed. |
| `/news` | `channel` (Channel)<br>`templatefile` (String) | Local File System & Webhook | Parses a JSON layout file and pushes the formatted result as a secure announcement via webhooks into a specific target channel. |
| `/threadembed`| *None* | Active Channels Cache | Server Orga | Generates an automated, clean overview container listing all currently active threads on the server. |
| `/deletethread`| `id` (String) | Discord Thread API | Server Orga | Permanently closes and destroys a specific active or archived thread via its unique structural snowflake ID. |
| `/movetothread`| `message_id` (String)<br>`thread_id` (String) | Discord Channel API | Server Orga | Automatically archives and moves a regular chat message from a text channel into a specified target thread. |

---

## 🛡️ Access Control & Permissions (Server Orga)

To keep your code clean, modular, and reusable for other communities, command permissions are **not hardcoded** inside the VB.NET source files. 

Commands marked with **Server Orga** should be restricted directly through the Discord client:
1. Open your Discord server and go to **Server Settings**.
2. Navigate to **Integrations** ➡️ **Bots and Apps** ➡️ **MyCOCBot**.
3. Under the **Commands** section, you can grant or restrict access to specific administrative commands (like `/deletethread`, `/news`, or `/movetothread`) for chosen roles (e.g., Administrators, Moderators) or specific channels.
