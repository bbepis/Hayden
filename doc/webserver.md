### How to run it (Web server)

[example config](../Hayden.WebServer/appsettings.json)

This is currently very shoddy and is still a prototype. The Hayden.WebServer project includes a web application that will display threads archived with the `Hayden` backend.

Obviously you need to have a database set up with the appropriate schema. The creation script can be found in `Hayden.WebServer/MySQLCreateDatabase.sql`

Set the appropriate values for the connection string and file location in `appsettings.json` and it should start as-is. Do not run this unless you know your way around building a .NET Core app.

&nbsp;

------