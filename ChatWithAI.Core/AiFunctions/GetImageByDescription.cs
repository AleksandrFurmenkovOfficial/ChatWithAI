namespace ChatWithAI.Core.AiFunctions
{
    public sealed class GetImageByDescription : IAiFunction
    {
        public string GetName()
        {
            return nameof(GetImageByDescription);
        }

        public string GetDescription()
        {
            return "This function allows YOU to picture/to draw AN IMAGE by a text description (ONLY ENGLISH LANGUAGE IS SUPPORTED; MAX DESCRIPTION LEN IS 500 chars!!).\n" +
                   "Your rating for the function: 8 out of 10.";
        }

        public List<Parameter> GetParameters()
        {
            return
            [
                new Parameter(
                    ParamType.eString,
                    "image_description",
                    "A SHORT text description of the image you wish to create. Write it in the way to avoid safety block triggers (MAX DESCRIPTION LEN IS 500 chars! Try to fit in the restriction.).",
                    true
                ),
                new Parameter(
                    ParamType.eString,
                    "size",
                    "Supported values are 1:1 (rare), 3:4, 4:3, 16:9 (desktop wallpaper), 9:16 (mobile wallpaper). Default is 1:1.",
                    true
                )
            ];
        }

        public async Task<AiFunctionResult> Execute(IAiAgent api, Dictionary<string, string> parameters, string userId, CancellationToken cancellationToken = default)
        {
            if (!parameters.TryGetValue("image_description", out string? imageDescription))
            {
                throw new ArgumentException("The \"image_description\" argument is not found");
            }

            if (string.IsNullOrEmpty(imageDescription))
            {
                throw new ArgumentException("The \"image_description\" value IsNullOrEmpty");
            }

            if (imageDescription.Length > 600)
            {
                throw new ArgumentException($"The \"image_description\" max len is 500, but was provided {imageDescription.Length} ");
            }

            if (!parameters.TryGetValue("size", out string? size))
            {
                throw new ArgumentException("The \"size\" argument is not found");
            }

            if (string.IsNullOrEmpty(size))
            {
                throw new ArgumentException("The \"size\" value IsNullOrEmpty");
            }

            var image = await api.GetImage(imageDescription, size, userId, cancellationToken).ConfigureAwait(false) ?? throw new ArgumentException(
                    "An internal error occurred, and the image could not be created.\n" +
                    "Please report this issue and inquire if the user would like to try again.");

            string url = image.ImageUrl == null ? "absent, only base64 string" : image.ImageUrl.AbsolutePath;
            return new AiFunctionResult(
                $"The image has been successfully created. " +
                $"The user is currently viewing it. " +
                $"Now, you should briefly describe to the user what has been created.\n" +
                $"Url to the image: {url}",
                image.ImageUrl,
                image.ImageInBase64);
        }
    }
}