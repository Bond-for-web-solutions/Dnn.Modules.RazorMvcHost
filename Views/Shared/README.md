# Razor MVC Host - Shared partials

Drop reusable Razor MVC partials here. They are auto-discovered by the
MVC view engine when scripts under `Views/Scripts/` call
`@Html.Partial("PartialName")`.

## Reusing the `<Select>` component

The MVC `_Select.cshtml` partial used by `Dnn.Modules.TableDZP` is a
drop-in fit for this module. To enable it here, copy:

    Dnn.Modules.TableDZP/Views/Shared/_Select.cshtml
        ↓
    Dnn.Modules.RazorMvcHost/Views/Shared/_Select.cshtml

Then any hosted script can render it via:

    @Html.Partial("_Select", null, new ViewDataDictionary {
        { "id",     "country" },
        { "name",   "country" },
        { "items",  myItems }
    })

See `Views/Scripts/SelectDemo.cshtml` for a full example.
