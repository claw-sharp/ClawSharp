// TS origin: ./tools/FileReadTool/FileReadTool.ts, ./tools/FileEditTool/types.ts, ./tools/FileWriteTool/FileWriteTool.ts
// TS parity status: TS-shaped schema metadata is ported for the current file tools; Edit and Write structured result parity still depends on the approved diff implementation path.
using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

internal static class FileToolSchemas
{
    public static JsonObject ReadInputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("file_path", ToolJsonSchemaFactory.String("The absolute path to the file to read")),
                ("offset", ToolJsonSchemaFactory.Integer(
                    "The line number to start reading from. Only provide if the file is too large to read at once",
                    minimum: 0)),
                ("limit", ToolJsonSchemaFactory.Integer(
                    "The number of lines to read. Only provide if the file is too large to read at once.",
                    minimum: 1)),
                ("pages", ToolJsonSchemaFactory.String(
                    "Page range for PDF files (e.g., \"1-5\", \"3\", \"10-20\"). Only applicable to PDF files. Maximum 20 pages per request."))
            ],
            required:
            [
                "file_path"
            ]);

    public static JsonObject ReadOutputSchema =>
        ToolJsonSchemaFactory.OneOf(
            ToolJsonSchemaFactory.StrictObject(
                [
                    ("type", ToolJsonSchemaFactory.StringEnum(["text"])),
                    ("file", ToolJsonSchemaFactory.StrictObject(
                        [
                            ("filePath", ToolJsonSchemaFactory.String("The path to the file that was read")),
                            ("content", ToolJsonSchemaFactory.String("The content of the file")),
                            ("numLines", ToolJsonSchemaFactory.Number("Number of lines in the returned content")),
                            ("startLine", ToolJsonSchemaFactory.Number("The starting line number")),
                            ("totalLines", ToolJsonSchemaFactory.Number("Total number of lines in the file"))
                        ],
                        required:
                        [
                            "filePath",
                            "content",
                            "numLines",
                            "startLine",
                            "totalLines"
                        ]))
                ],
                required:
                [
                    "type",
                    "file"
                ]),
            ToolJsonSchemaFactory.StrictObject(
                [
                    ("type", ToolJsonSchemaFactory.StringEnum(["image"])),
                    ("file", ToolJsonSchemaFactory.StrictObject(
                        [
                            ("base64", ToolJsonSchemaFactory.String("Base64-encoded image data")),
                            ("type", ToolJsonSchemaFactory.StringEnum(
                                [
                                    "image/jpeg",
                                    "image/png",
                                    "image/gif",
                                    "image/webp"
                                ],
                                "The MIME type of the image")),
                            ("originalSize", ToolJsonSchemaFactory.Number("Original file size in bytes")),
                            ("dimensions", ToolJsonSchemaFactory.StrictObject(
                                [
                                    ("originalWidth", ToolJsonSchemaFactory.Number("Original image width in pixels")),
                                    ("originalHeight", ToolJsonSchemaFactory.Number("Original image height in pixels")),
                                    ("displayWidth", ToolJsonSchemaFactory.Number("Displayed image width in pixels (after resizing)")),
                                    ("displayHeight", ToolJsonSchemaFactory.Number("Displayed image height in pixels (after resizing)"))
                                ],
                                description: "Image dimension info for coordinate mapping"))
                        ],
                        required:
                        [
                            "base64",
                            "type",
                            "originalSize"
                        ]))
                ],
                required:
                [
                    "type",
                    "file"
                ]),
            ToolJsonSchemaFactory.StrictObject(
                [
                    ("type", ToolJsonSchemaFactory.StringEnum(["notebook"])),
                    ("file", ToolJsonSchemaFactory.StrictObject(
                        [
                            ("filePath", ToolJsonSchemaFactory.String("The path to the notebook file")),
                            ("cells", ToolJsonSchemaFactory.Array(
                                new JsonObject(),
                                "Array of notebook cells"))
                        ],
                        required:
                        [
                            "filePath",
                            "cells"
                        ]))
                ],
                required:
                [
                    "type",
                    "file"
                ]),
            ToolJsonSchemaFactory.StrictObject(
                [
                    ("type", ToolJsonSchemaFactory.StringEnum(["pdf"])),
                    ("file", ToolJsonSchemaFactory.StrictObject(
                        [
                            ("filePath", ToolJsonSchemaFactory.String("The path to the PDF file")),
                            ("base64", ToolJsonSchemaFactory.String("Base64-encoded PDF data")),
                            ("originalSize", ToolJsonSchemaFactory.Number("Original file size in bytes"))
                        ],
                        required:
                        [
                            "filePath",
                            "base64",
                            "originalSize"
                        ]))
                ],
                required:
                [
                    "type",
                    "file"
                ]),
            ToolJsonSchemaFactory.StrictObject(
                [
                    ("type", ToolJsonSchemaFactory.StringEnum(["parts"])),
                    ("file", ToolJsonSchemaFactory.StrictObject(
                        [
                            ("filePath", ToolJsonSchemaFactory.String("The path to the PDF file")),
                            ("originalSize", ToolJsonSchemaFactory.Number("Original file size in bytes")),
                            ("count", ToolJsonSchemaFactory.Number("Number of pages extracted")),
                            ("outputDir", ToolJsonSchemaFactory.String("Directory containing extracted page images"))
                        ],
                        required:
                        [
                            "filePath",
                            "originalSize",
                            "count",
                            "outputDir"
                        ]))
                ],
                required:
                [
                    "type",
                    "file"
                ]),
            ToolJsonSchemaFactory.StrictObject(
                [
                    ("type", ToolJsonSchemaFactory.StringEnum(["file_unchanged"])),
                    ("file", ToolJsonSchemaFactory.StrictObject(
                        [
                            ("filePath", ToolJsonSchemaFactory.String("The path to the file"))
                        ],
                        required:
                        [
                            "filePath"
                        ]))
                ],
                required:
                [
                    "type",
                    "file"
                ]));

    public static JsonObject EditInputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("file_path", ToolJsonSchemaFactory.String("The absolute path to the file to modify")),
                ("old_string", ToolJsonSchemaFactory.String("The text to replace")),
                ("new_string", ToolJsonSchemaFactory.String("The text to replace it with (must be different from old_string)")),
                ("replace_all", ToolJsonSchemaFactory.Boolean("Replace all occurrences of old_string (default false)", defaultValue: false))
            ],
            required:
            [
                "file_path",
                "old_string",
                "new_string"
            ]);

    public static JsonObject EditOutputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("filePath", ToolJsonSchemaFactory.String("The file path that was edited")),
                ("oldString", ToolJsonSchemaFactory.String("The original string that was replaced")),
                ("newString", ToolJsonSchemaFactory.String("The new string that replaced it")),
                ("originalFile", ToolJsonSchemaFactory.String("The original file contents before editing")),
                ("structuredPatch", ToolJsonSchemaFactory.Array(HunkSchema, "Diff patch showing the changes")),
                ("userModified", ToolJsonSchemaFactory.Boolean("Whether the user modified the proposed changes")),
                ("replaceAll", ToolJsonSchemaFactory.Boolean("Whether all occurrences were replaced")),
                ("gitDiff", GitDiffSchema)
            ],
            required:
            [
                "filePath",
                "oldString",
                "newString",
                "originalFile",
                "structuredPatch",
                "userModified",
                "replaceAll"
            ]);

    public static JsonObject WriteInputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("file_path", ToolJsonSchemaFactory.String(
                    "The absolute path to the file to write (must be absolute, not relative)")),
                ("content", ToolJsonSchemaFactory.String("The content to write to the file"))
            ],
            required:
            [
                "file_path",
                "content"
            ]);

    public static JsonObject WriteOutputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("type", ToolJsonSchemaFactory.StringEnum(
                    [
                        "create",
                        "update"
                    ],
                    "Whether a new file was created or an existing file was updated")),
                ("filePath", ToolJsonSchemaFactory.String("The path to the file that was written")),
                ("content", ToolJsonSchemaFactory.String("The content that was written to the file")),
                ("structuredPatch", ToolJsonSchemaFactory.Array(HunkSchema, "Diff patch showing the changes")),
                ("originalFile", ToolJsonSchemaFactory.Nullable(
                    ToolJsonSchemaFactory.String(),
                    "The original file content before the write (null for new files)")),
                ("gitDiff", GitDiffSchema)
            ],
            required:
            [
                "type",
                "filePath",
                "content",
                "structuredPatch",
                "originalFile"
            ]);

    private static JsonObject HunkSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("oldStart", ToolJsonSchemaFactory.Number()),
                ("oldLines", ToolJsonSchemaFactory.Number()),
                ("newStart", ToolJsonSchemaFactory.Number()),
                ("newLines", ToolJsonSchemaFactory.Number()),
                ("lines", ToolJsonSchemaFactory.Array(ToolJsonSchemaFactory.String()))
            ],
            required:
            [
                "oldStart",
                "oldLines",
                "newStart",
                "newLines",
                "lines"
            ]);

    private static JsonObject GitDiffSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("filename", ToolJsonSchemaFactory.String()),
                ("status", ToolJsonSchemaFactory.StringEnum(
                    [
                        "modified",
                        "added"
                    ])),
                ("additions", ToolJsonSchemaFactory.Number()),
                ("deletions", ToolJsonSchemaFactory.Number()),
                ("changes", ToolJsonSchemaFactory.Number()),
                ("patch", ToolJsonSchemaFactory.String()),
                ("repository", ToolJsonSchemaFactory.Nullable(
                    ToolJsonSchemaFactory.String(),
                    "GitHub owner/repo when available"))
            ],
            required:
            [
                "filename",
                "status",
                "additions",
                "deletions",
                "changes",
                "patch"
            ]);
}
